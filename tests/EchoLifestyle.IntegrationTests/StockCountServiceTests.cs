using EchoLifestyle.Application.Inventory.Adjustments;
using EchoLifestyle.Application.Inventory.Counts;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Counting the shelf.
///
/// Two rules carry the whole design, and both have a test here that would fail
/// loudly if somebody "simplified" them: a blank line is skipped rather than
/// counted as zero, and posting applies the variance as a delta rather than
/// setting the balance to the counted figure.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StockCountServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public StockCountServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _fixture.CurrentUser.IsAuthenticated = true;
        _fixture.CurrentUser.UserType = UserType.Staff;
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.UserId = 1;
        _fixture.CurrentUser.UserName = "test-owner";
        _fixture.CurrentUser.Grants.Clear();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        _branchId = await db.Branches.Where(b => b.IsActive).Select(b => b.Id).FirstAsync();
        _warehouseId = await db.Warehouses.Where(w => w.IsActive).Select(w => w.Id).FirstAsync();

        _fixture.CurrentUser.BranchIds = [_branchId];

        // Counts are exclusive per warehouse, and this collection shares one.
        // Anything a previous test left open would refuse every count here, so
        // the slate is cleared rather than depending on running order.
        await CloseAnyOpenCountAsync(scope.ServiceProvider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task CloseAnyOpenCountAsync(IServiceProvider services)
    {
        var counts = services.GetRequiredService<StockCountService>();
        var open = await counts.FindOpenAsync(_warehouseId);

        if (open is not null)
        {
            await counts.CancelAsync(open.Value, "Left open by an earlier test.");
        }
    }

    private StartCountRequest StartRequest(
        StockCountScope scope = StockCountScope.Everything,
        long? scopeId = null) => new()
    {
        WarehouseId = _warehouseId,
        BranchId = _branchId,
        CountDate = new DateOnly(2026, 9, 7),
        Scope = scope,
        ScopeId = scopeId,
        Notes = "Monthly count.",
    };

    private Task<decimal> OnHandAsync(EchoDbContext db, long batchId) =>
        db.StockBalances
            .Where(b => b.StockBatchId == batchId && b.WarehouseId == _warehouseId)
            .Select(b => b.QuantityOnHand)
            .FirstAsync();

    private async Task<(long VariantId, long BatchId, long BrandId)> StockedAsync(
        IServiceProvider services,
        decimal quantity = 10m,
        decimal unitCost = 200m)
    {
        var suffix = InventoryTestData.Unique();
        var supplierId = await InventoryTestData.SupplierAsync(services, suffix);
        var variant = await InventoryTestData.VariantAsync(services, suffix);

        var batchId = await InventoryTestData.ReceiveAsync(
            services, supplierId, _warehouseId, _branchId, variant.VariantId, quantity, unitCost);

        return (variant.VariantId, batchId, variant.BrandId);
    }

    private static StockCountLineDetail LineFor(StockCountDetail detail, long batchId) =>
        detail.Lines.Single(l => l.StockBatchId == batchId);

    // -----------------------------------------------------------------------
    // Generating a sheet
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_sheet_freezes_what_the_system_believes()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider, quantity: 12m, unitCost: 250m);

        var started = await counts.StartAsync(StartRequest());
        Assert.True(started.Succeeded, started.Error);

        var detail = await counts.GetAsync(started.Value);
        var line = LineFor(detail!, batchId);

        Assert.Equal(12m, line.SystemQuantity);
        Assert.Equal(12m, line.CurrentSystemQuantity);
        Assert.Null(line.CountedQuantity);

        // Cost is frozen with the quantity, so a variance is valued at what the
        // stock cost when it was counted rather than whatever it costs later.
        Assert.Equal(250m, line.LandedUnitCost);
        Assert.Equal(StockCountStatus.Counting, detail.Status);
    }

    [Fact]
    public async Task A_second_open_count_for_the_same_warehouse_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        await StockedAsync(scope.ServiceProvider);

        var first = await counts.StartAsync(StartRequest());
        Assert.True(first.Succeeded, first.Error);

        var second = await counts.StartAsync(StartRequest());

        Assert.False(second.Succeeded);
        Assert.Contains("still open", second.Error!);
    }

    [Fact]
    public async Task A_count_scoped_to_a_brand_leaves_everything_else_off_the_sheet()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        var mine = await StockedAsync(scope.ServiceProvider);
        var other = await StockedAsync(scope.ServiceProvider);

        var started = await counts.StartAsync(
            StartRequest(StockCountScope.Brand, mine.BrandId));

        Assert.True(started.Succeeded, started.Error);

        var detail = await counts.GetAsync(started.Value);

        Assert.Contains(detail!.Lines, l => l.StockBatchId == mine.BatchId);
        Assert.DoesNotContain(detail.Lines, l => l.StockBatchId == other.BatchId);
    }

    [Fact]
    public async Task A_scope_with_no_stock_in_it_is_refused_rather_than_producing_an_empty_sheet()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        // A brand with a product but nothing received against it.
        var suffix = InventoryTestData.Unique();
        var variant = await InventoryTestData.VariantAsync(scope.ServiceProvider, suffix);

        var started = await counts.StartAsync(
            StartRequest(StockCountScope.Brand, variant.BrandId));

        Assert.False(started.Succeeded);
        Assert.Contains("no stock to count", started.Error!);
    }

    // -----------------------------------------------------------------------
    // Posting
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_shortfall_posts_as_a_movement_and_corrects_the_balance()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider, quantity: 10m, unitCost: 200m);

        var started = await counts.StartAsync(StartRequest());
        var detail = await counts.GetAsync(started.Value);
        var line = LineFor(detail!, batchId);

        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = line.Id, CountedQuantity = 8m }]);

        var posted = await counts.PostAsync(started.Value);

        Assert.True(posted.Succeeded, posted.Error);
        Assert.Equal(1, posted.Value!.LinesPosted);
        Assert.Equal(-2m, posted.Value.NetUnits);
        Assert.Equal(-400m, posted.Value.NetValue);

        Assert.Equal(8m, await OnHandAsync(db, batchId));

        var entry = await db.StockLedger
            .SingleAsync(e => e.StockBatchId == batchId
                              && e.DocumentType == StockDocumentType.StockCount);

        Assert.Equal(-2m, entry.QuantityChange);
        Assert.Equal(StockMovementType.AdjustmentOut, entry.MovementType);
    }

    [Fact]
    public async Task A_line_counted_the_same_moves_nothing()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider);

        var started = await counts.StartAsync(StartRequest());
        var detail = await counts.GetAsync(started.Value);
        var line = LineFor(detail!, batchId);

        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = line.Id, CountedQuantity = 10m }]);

        var posted = await counts.PostAsync(started.Value);

        Assert.True(posted.Succeeded, posted.Error);
        Assert.Equal(0, posted.Value!.LinesPosted);
        Assert.Equal(10m, await OnHandAsync(db, batchId));

        // A count that agrees writes no ledger entry. The ledger records
        // movements, and nothing moved.
        Assert.False(await db.StockLedger.AnyAsync(
            e => e.StockBatchId == batchId && e.DocumentType == StockDocumentType.StockCount));
    }

    /// <summary>
    /// The expensive default this design exists to avoid: reading a blank line
    /// as "none found" would write off every batch nobody reached.
    /// </summary>
    [Fact]
    public async Task An_uncounted_line_is_skipped_rather_than_written_off()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var counted = await StockedAsync(scope.ServiceProvider, quantity: 10m);
        var neverReached = await StockedAsync(scope.ServiceProvider, quantity: 7m);

        var started = await counts.StartAsync(StartRequest());
        var detail = await counts.GetAsync(started.Value);

        await counts.SaveCountsAsync(started.Value,
        [
            new CountEntryInput
            {
                LineId = LineFor(detail!, counted.BatchId).Id,
                CountedQuantity = 9m,
            },
        ]);

        var posted = await counts.PostAsync(started.Value);

        Assert.True(posted.Succeeded, posted.Error);
        Assert.Equal(1, posted.Value!.LinesPosted);

        Assert.Equal(9m, await OnHandAsync(db, counted.BatchId));

        // Untouched. Not zero, not written off, not even a ledger entry.
        Assert.Equal(7m, await OnHandAsync(db, neverReached.BatchId));
        Assert.False(await db.StockLedger.AnyAsync(
            e => e.StockBatchId == neverReached.BatchId
                 && e.DocumentType == StockDocumentType.StockCount));
    }

    [Fact]
    public async Task Counting_zero_writes_the_batch_off_because_that_is_what_it_means()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider, quantity: 5m);

        var started = await counts.StartAsync(StartRequest());
        var detail = await counts.GetAsync(started.Value);

        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = LineFor(detail!, batchId).Id, CountedQuantity = 0m }]);

        var posted = await counts.PostAsync(started.Value);

        Assert.True(posted.Succeeded, posted.Error);
        Assert.Equal(0m, await OnHandAsync(db, batchId));
    }

    /// <summary>
    /// The second rule that carries the design. A count takes an afternoon and
    /// sales happen during it; setting the balance to the counted figure would
    /// silently put back everything sold in the meantime.
    /// </summary>
    [Fact]
    public async Task Stock_that_moves_mid_count_is_not_undone_by_posting()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId, _) = await StockedAsync(scope.ServiceProvider, quantity: 10m);

        var started = await counts.StartAsync(StartRequest());
        var detail = await counts.GetAsync(started.Value);
        var line = LineFor(detail!, batchId);

        // Counted at 8: two are genuinely missing.
        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = line.Id, CountedQuantity = 8m }]);

        // Three leave the building before the count is posted.
        var meanwhile = await adjustments.SubmitAsync(new SubmitAdjustmentRequest
        {
            WarehouseId = _warehouseId,
            BranchId = _branchId,
            AdjustmentDate = new DateOnly(2026, 9, 7),
            Reason = StockAdjustmentReason.TesterOrSample,
            ReasonNotes = "Three opened as testers while the count was running.",
            Lines =
            [
                new AdjustmentLineInput
                {
                    ProductVariantId = variantId,
                    StockBatchId = batchId,
                    QuantityChange = -3m,
                },
            ],
        });

        Assert.True(meanwhile.Succeeded, meanwhile.Error);
        Assert.Equal(7m, await OnHandAsync(db, batchId));

        var posted = await counts.PostAsync(started.Value);
        Assert.True(posted.Succeeded, posted.Error);

        // 10 believed, 8 found, so 2 missing. 3 legitimately left afterwards.
        // 7 - 2 = 5. Setting the balance to the counted 8 would have invented
        // three units and hidden the testers.
        Assert.Equal(5m, await OnHandAsync(db, batchId));
    }

    [Fact]
    public async Task Drift_since_the_snapshot_is_visible_before_posting()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId, _) = await StockedAsync(scope.ServiceProvider, quantity: 10m);

        var started = await counts.StartAsync(StartRequest());

        await adjustments.SubmitAsync(new SubmitAdjustmentRequest
        {
            WarehouseId = _warehouseId,
            BranchId = _branchId,
            AdjustmentDate = new DateOnly(2026, 9, 7),
            Reason = StockAdjustmentReason.Damaged,
            ReasonNotes = "One dropped while the count was running.",
            Lines =
            [
                new AdjustmentLineInput
                {
                    ProductVariantId = variantId,
                    StockBatchId = batchId,
                    QuantityChange = -1m,
                },
            ],
        });

        var detail = await counts.GetAsync(started.Value);
        var line = LineFor(detail!, batchId);

        Assert.Equal(10m, line.SystemQuantity);
        Assert.Equal(9m, line.CurrentSystemQuantity);
        Assert.True(line.HasDrifted);
        Assert.Contains(detail!.Drifted, l => l.StockBatchId == batchId);
    }

    [Fact]
    public async Task A_count_with_nothing_entered_cannot_be_posted()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        await StockedAsync(scope.ServiceProvider);

        var started = await counts.StartAsync(StartRequest());
        var posted = await counts.PostAsync(started.Value);

        Assert.False(posted.Succeeded);
        Assert.Contains("has been counted", posted.Error!);
    }

    [Fact]
    public async Task Posting_twice_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider);

        var started = await counts.StartAsync(StartRequest());
        var detail = await counts.GetAsync(started.Value);

        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = LineFor(detail!, batchId).Id, CountedQuantity = 6m }]);

        var first = await counts.PostAsync(started.Value);
        Assert.True(first.Succeeded, first.Error);

        var second = await counts.PostAsync(started.Value);

        Assert.False(second.Succeeded);
        Assert.Contains("already posted", second.Error!);
    }

    // -----------------------------------------------------------------------
    // Editing and abandoning
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Clearing_a_figure_puts_the_line_back_to_uncounted()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider);

        var started = await counts.StartAsync(StartRequest());
        var lineId = LineFor((await counts.GetAsync(started.Value))!, batchId).Id;

        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = lineId, CountedQuantity = 4m }]);

        Assert.Equal(4m, LineFor((await counts.GetAsync(started.Value))!, batchId).CountedQuantity);

        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = lineId, CountedQuantity = null }]);

        // Back to blank, not zero. Those mean different things and only one of
        // them is safe to post.
        Assert.Null(LineFor((await counts.GetAsync(started.Value))!, batchId).CountedQuantity);
    }

    [Fact]
    public async Task A_negative_count_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider);

        var started = await counts.StartAsync(StartRequest());
        var lineId = LineFor((await counts.GetAsync(started.Value))!, batchId).Id;

        var saved = await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = lineId, CountedQuantity = -1m }]);

        Assert.False(saved.Succeeded);
    }

    [Fact]
    public async Task Cancelling_moves_nothing_and_frees_the_warehouse()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider, quantity: 9m);

        var started = await counts.StartAsync(StartRequest());
        var lineId = LineFor((await counts.GetAsync(started.Value))!, batchId).Id;

        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = lineId, CountedQuantity = 2m }]);

        var cancelled = await counts.CancelAsync(started.Value, "Interrupted.");

        Assert.True(cancelled.Succeeded, cancelled.Error);
        Assert.Equal(9m, await OnHandAsync(db, batchId));
        Assert.Null(await counts.FindOpenAsync(_warehouseId));

        // And the warehouse is free for another.
        var next = await counts.StartAsync(StartRequest());
        Assert.True(next.Succeeded, next.Error);
    }

    [Fact]
    public async Task A_posted_count_cannot_be_cancelled()
    {
        await using var scope = _fixture.CreateScope();
        var counts = scope.ServiceProvider.GetRequiredService<StockCountService>();

        var (_, batchId, _) = await StockedAsync(scope.ServiceProvider);

        var started = await counts.StartAsync(StartRequest());
        var lineId = LineFor((await counts.GetAsync(started.Value))!, batchId).Id;

        await counts.SaveCountsAsync(started.Value,
            [new CountEntryInput { LineId = lineId, CountedQuantity = 3m }]);

        Assert.True((await counts.PostAsync(started.Value)).Succeeded);

        var cancelled = await counts.CancelAsync(started.Value, "Changed my mind.");

        Assert.False(cancelled.Succeeded);
        Assert.Contains("adjustment", cancelled.Error!);
    }
}
