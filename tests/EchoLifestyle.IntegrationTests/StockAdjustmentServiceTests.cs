using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Inventory.Adjustments;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Stock changing for a reason that is not a purchase or a sale.
///
/// The rules worth holding onto: the reason supplies the direction, an
/// adjustment can never remove more than exists, nothing moves before approval,
/// and cost is frozen onto the line when it posts rather than when it is
/// written.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StockAdjustmentServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private long _branchId;
    private long _warehouseId;

    public StockAdjustmentServiceTests(DatabaseFixture fixture)
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
    }

    public Task DisposeAsync()
    {
        // Left as the fixture found it: the current user is shared across the
        // collection, and a test that flips it must not leak that to the next.
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.Grants.Clear();

        return Task.CompletedTask;
    }

    private SubmitAdjustmentRequest Request(
        long variantId,
        long batchId,
        decimal quantity,
        StockAdjustmentReason reason = StockAdjustmentReason.Damaged) => new()
    {
        WarehouseId = _warehouseId,
        BranchId = _branchId,
        AdjustmentDate = new DateOnly(2026, 9, 7),
        Reason = reason,
        ReasonNotes = "Carton crushed under the top shelf during Tuesday's delivery.",
        Lines =
        [
            new AdjustmentLineInput
            {
                ProductVariantId = variantId,
                StockBatchId = batchId,
                QuantityChange = quantity,
            },
        ],
    };

    private Task<decimal> OnHandAsync(EchoDbContext db, long batchId) =>
        db.StockBalances
            .Where(b => b.StockBatchId == batchId && b.WarehouseId == _warehouseId)
            .Select(b => b.QuantityOnHand)
            .FirstAsync();

    private async Task<(long VariantId, long BatchId)> StockedAsync(
        IServiceProvider services,
        decimal quantity = 10m,
        decimal unitCost = 200m)
    {
        var suffix = InventoryTestData.Unique();
        var supplierId = await InventoryTestData.SupplierAsync(services, suffix);
        var variant = await InventoryTestData.VariantAsync(services, suffix);

        var batchId = await InventoryTestData.ReceiveAsync(
            services, supplierId, _warehouseId, _branchId, variant.VariantId, quantity, unitCost);

        return (variant.VariantId, batchId);
    }

    // -----------------------------------------------------------------------
    // Posting
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_approver_posts_immediately_and_the_balance_follows_the_ledger()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var result = await adjustments.SubmitAsync(
            Request(variantId, batchId, -3m));

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.WasPosted);

        Assert.Equal(7m, await OnHandAsync(db, batchId));

        var entry = await db.StockLedger
            .SingleAsync(e => e.StockBatchId == batchId
                              && e.DocumentType == StockDocumentType.StockAdjustment);

        Assert.Equal(-3m, entry.QuantityChange);
        Assert.Equal(200m, entry.UnitCost);
        Assert.Equal(-600m, entry.ValueChange);

        // Damage is a write-off, not a bare adjustment out. "We destroyed it"
        // and "the number was wrong" are different questions, and a report that
        // cannot separate them is not worth running.
        Assert.Equal(StockMovementType.WriteOff, entry.MovementType);
    }

    [Fact]
    public async Task A_correction_out_is_an_adjustment_rather_than_a_write_off()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var result = await adjustments.SubmitAsync(
            Request(variantId, batchId, -2m, StockAdjustmentReason.Correction));

        Assert.True(result.Succeeded, result.Error);

        var entry = await db.StockLedger
            .SingleAsync(e => e.StockBatchId == batchId
                              && e.DocumentType == StockDocumentType.StockAdjustment);

        Assert.Equal(StockMovementType.AdjustmentOut, entry.MovementType);
    }

    [Fact]
    public async Task Found_stock_goes_back_into_the_batch_it_left_from()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var result = await adjustments.SubmitAsync(
            Request(variantId, batchId, 4m, StockAdjustmentReason.FoundExtra));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(14m, await OnHandAsync(db, batchId));

        var entry = await db.StockLedger
            .SingleAsync(e => e.StockBatchId == batchId
                              && e.DocumentType == StockDocumentType.StockAdjustment);

        Assert.Equal(StockMovementType.AdjustmentIn, entry.MovementType);
    }

    [Fact]
    public async Task Cost_is_frozen_onto_the_line_when_it_posts()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider, unitCost: 350m);

        var result = await adjustments.SubmitAsync(Request(variantId, batchId, -2m));

        Assert.True(result.Succeeded, result.Error);

        var line = await db.StockAdjustmentLines
            .SingleAsync(l => l.StockAdjustmentId == result.Value!.Id);

        Assert.Equal(350m, line.UnitCost);
        Assert.Equal(-700m, line.ValueChange);
    }

    // -----------------------------------------------------------------------
    // Approval
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Without_the_approval_permission_nothing_moves_until_somebody_approves()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        AsRequesterOnly();

        var submitted = await adjustments.SubmitAsync(Request(variantId, batchId, -3m));

        Assert.True(submitted.Succeeded, submitted.Error);
        Assert.False(submitted.Value!.WasPosted);

        // The whole point of a pending document: the shelf has not changed.
        Assert.Equal(10m, await OnHandAsync(db, batchId));
        Assert.False(await db.StockLedger.AnyAsync(
            e => e.StockBatchId == batchId && e.DocumentType == StockDocumentType.StockAdjustment));

        AsOwner();

        var approved = await adjustments.ApproveAsync(submitted.Value.Id, "Checked it myself.");

        Assert.True(approved.Succeeded, approved.Error);
        Assert.Equal(7m, await OnHandAsync(db, batchId));
    }

    [Fact]
    public async Task Rejecting_moves_nothing_and_keeps_the_reason()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        AsRequesterOnly();
        var submitted = await adjustments.SubmitAsync(Request(variantId, batchId, -3m));
        Assert.True(submitted.Succeeded, submitted.Error);

        AsOwner();
        var rejected = await adjustments.RejectAsync(
            submitted.Value!.Id, "Count it again before writing it off.");

        Assert.True(rejected.Succeeded, rejected.Error);
        Assert.Equal(10m, await OnHandAsync(db, batchId));

        var detail = await adjustments.GetAsync(submitted.Value.Id);

        Assert.Equal(StockAdjustmentStatus.Rejected, detail!.Status);
        Assert.Equal("Count it again before writing it off.", detail.DecisionNotes);
    }

    [Fact]
    public async Task A_rejection_needs_a_reason()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        AsRequesterOnly();
        var submitted = await adjustments.SubmitAsync(Request(variantId, batchId, -1m));

        AsOwner();
        var rejected = await adjustments.RejectAsync(submitted.Value!.Id, "   ");

        Assert.False(rejected.Succeeded);
    }

    [Fact]
    public async Task An_adjustment_already_decided_cannot_be_decided_again()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var posted = await adjustments.SubmitAsync(Request(variantId, batchId, -1m));
        Assert.True(posted.Succeeded, posted.Error);

        var again = await adjustments.ApproveAsync(posted.Value!.Id, null);

        Assert.False(again.Succeeded);
        Assert.Contains("posted", again.Error!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The reason validation at submission is a courtesy; the one at approval is
    /// the rule. A document can sit pending while the stock it names walks out
    /// of the door.
    /// </summary>
    [Fact]
    public async Task Stock_that_disappears_while_the_adjustment_waits_stops_it_posting()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        AsRequesterOnly();
        var pending = await adjustments.SubmitAsync(Request(variantId, batchId, -8m));
        Assert.True(pending.Succeeded, pending.Error);

        AsOwner();

        // Something else takes most of the stock while the first document waits.
        var meanwhile = await adjustments.SubmitAsync(
            Request(variantId, batchId, -9m, StockAdjustmentReason.Stolen));
        Assert.True(meanwhile.Succeeded, meanwhile.Error);
        Assert.Equal(1m, await OnHandAsync(db, batchId));

        var approved = await adjustments.ApproveAsync(pending.Value!.Id, null);

        Assert.False(approved.Succeeded);
        Assert.Contains("since this adjustment was written", approved.Error!);
        Assert.Equal(1m, await OnHandAsync(db, batchId));
    }

    // -----------------------------------------------------------------------
    // What is refused
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_outbound_reason_refuses_a_positive_quantity()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var result = await adjustments.SubmitAsync(
            Request(variantId, batchId, 5m, StockAdjustmentReason.Damaged));

        Assert.False(result.Succeeded);
        Assert.Contains("only takes stock out", result.Error!);
    }

    [Fact]
    public async Task Found_extra_refuses_a_negative_quantity()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var result = await adjustments.SubmitAsync(
            Request(variantId, batchId, -5m, StockAdjustmentReason.FoundExtra));

        Assert.False(result.Succeeded);
        Assert.Contains("only brings stock in", result.Error!);
    }

    [Fact]
    public async Task An_adjustment_cannot_remove_more_than_is_on_the_shelf()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var result = await adjustments.SubmitAsync(Request(variantId, batchId, -11m));

        Assert.False(result.Succeeded);
        Assert.Contains("only 10", result.Error!);

        // Nothing half-written left behind.
        Assert.Equal(10m, await OnHandAsync(db, batchId));
        Assert.False(await db.StockAdjustments.AnyAsync(
            a => a.Lines.Any(l => l.StockBatchId == batchId)));
    }

    [Fact]
    public async Task The_same_batch_twice_on_one_document_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var request = Request(variantId, batchId, -1m);

        request.Lines =
        [
            .. request.Lines,
            new AdjustmentLineInput
            {
                ProductVariantId = variantId,
                StockBatchId = batchId,
                QuantityChange = -2m,
            },
        ];

        var result = await adjustments.SubmitAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains("twice", result.Error!);
    }

    [Fact]
    public async Task An_explanation_is_required()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var request = Request(variantId, batchId, -1m);
        request.ReasonNotes = "   ";

        var result = await adjustments.SubmitAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SubmitAdjustmentRequest.ReasonNotes), result.Field);
    }

    [Fact]
    public async Task An_adjustment_cannot_be_dated_in_the_future()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var request = Request(variantId, batchId, -1m);
        request.AdjustmentDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);

        var result = await adjustments.SubmitAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains("future", result.Error!);
    }

    [Fact]
    public async Task A_batch_belonging_to_another_product_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var first = await StockedAsync(scope.ServiceProvider);
        var second = await StockedAsync(scope.ServiceProvider);

        // The batch of one product against the variant of another: a stale or
        // tampered form, and posting either half of it would be a guess.
        var result = await adjustments.SubmitAsync(
            Request(first.VariantId, second.BatchId, -1m));

        Assert.False(result.Succeeded);
        Assert.Contains("does not belong", result.Error!);
    }

    // -----------------------------------------------------------------------
    // Numbering
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Numbers_are_sequential_within_a_month()
    {
        await using var scope = _fixture.CreateScope();
        var adjustments = scope.ServiceProvider.GetRequiredService<StockAdjustmentService>();

        var (variantId, batchId) = await StockedAsync(scope.ServiceProvider);

        var first = await adjustments.SubmitAsync(Request(variantId, batchId, -1m));
        var second = await adjustments.SubmitAsync(Request(variantId, batchId, -1m));

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);

        var firstNumber = (await adjustments.GetAsync(first.Value!.Id))!.Number;
        var secondNumber = (await adjustments.GetAsync(second.Value!.Id))!.Number;

        Assert.StartsWith("ADJ-2609-", firstNumber);
        Assert.NotEqual(firstNumber, secondNumber);
        Assert.True(string.CompareOrdinal(secondNumber, firstNumber) > 0);
    }

    private void AsOwner()
    {
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.Grants.Clear();
    }

    /// <summary>
    /// Somebody who may write an adjustment but not approve one - the whole
    /// reason the pending state exists.
    /// </summary>
    private void AsRequesterOnly()
    {
        _fixture.CurrentUser.IsOwner = false;
        _fixture.CurrentUser.Grants.Clear();
        _fixture.CurrentUser.Grants.Add(Permissions.Inventory.AdjustmentCreate);
    }
}
