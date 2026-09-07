using System.Globalization;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Inventory.Adjustments;

/// <summary>
/// Stock changing for a reason that is not a purchase or a sale.
///
/// Two paths, one posting routine. Somebody who can approve adjustments submits
/// and posts in a single action; somebody who cannot leaves a document waiting.
/// Both end in <see cref="PostAsync"/>, so an adjustment posted by an owner in
/// one click and one approved a day later produce identical ledger entries.
///
/// Validation runs twice on the second path - once at submission and again at
/// approval - because a document can sit pending while the stock it refers to is
/// sold. The second check is the one that matters.
/// </summary>
public partial class StockAdjustmentService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly StockMovementWriter _movements;

    public StockAdjustmentService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        IAuditLogger audit,
        StockMovementWriter movements)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _movements = movements;
    }

    /// <summary>Whether the acting user may approve, and therefore post directly.</summary>
    public bool CanApprove =>
        _currentUser.IsOwner || _currentUser.HasPermission(Permissions.Inventory.AdjustmentApprove);

    /// <summary>
    /// Writes the adjustment, and posts it too when the acting user is allowed
    /// to approve their own.
    /// </summary>
    public async Task<OperationResult<AdjustmentSubmission>> SubmitAsync(
        SubmitAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(request, cancellationToken);

        if (!prepared.Succeeded)
        {
            return OperationResult<AdjustmentSubmission>.Failure(prepared.Error!, prepared.Field);
        }

        var plan = prepared.Value!;
        var postImmediately = CanApprove;

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                var adjustment = await CreateAsync(plan, token);

                if (!postImmediately)
                {
                    await _audit.LogAsync(
                        AuditActions.StockAdjustmentSubmitted,
                        nameof(StockAdjustment),
                        adjustment.Id.ToString(CultureInfo.InvariantCulture),
                        $"Submitted {adjustment.Number} ({adjustment.Reason}) for approval: "
                        + $"{plan.Lines.Count} line(s).",
                        new { adjustment.Number, adjustment.Reason, adjustment.ReasonNotes },
                        plan.BranchId,
                        token);

                    await _db.SaveChangesAsync(token);

                    return OperationResult<AdjustmentSubmission>.Success(
                        new AdjustmentSubmission { Id = adjustment.Id, WasPosted = false });
                }

                var posted = await PostAsync(adjustment, decisionNotes: null, selfApproved: true, token);

                return posted.Succeeded
                    ? OperationResult<AdjustmentSubmission>.Success(
                        new AdjustmentSubmission { Id = adjustment.Id, WasPosted = true })
                    : OperationResult<AdjustmentSubmission>.Failure(posted.Error!, posted.Field);
            },
            cancellationToken);
    }

    /// <summary>Approves a pending adjustment, which posts it.</summary>
    public async Task<OperationResult> ApproveAsync(
        long id,
        string? decisionNotes,
        CancellationToken cancellationToken = default)
    {
        var adjustment = await LoadForDecisionAsync(id, cancellationToken);

        if (adjustment is null)
        {
            return OperationResult.Failure("That adjustment no longer exists.");
        }

        if (adjustment.Status != StockAdjustmentStatus.PendingApproval)
        {
            return OperationResult.Failure(
                $"{adjustment.Number} is already {adjustment.Status.ToString().ToLowerInvariant()} "
                + "and cannot be approved again.");
        }

        // An author approving their own work is legitimate here - there are two
        // partners and one warehouse - but it is recorded rather than hidden, so
        // the pattern can be seen if the business ever grows into needing
        // separation of duties.
        var selfApproved = adjustment.CreatedByUserId is not null
                           && adjustment.CreatedByUserId == _currentUser.UserId;

        return await _db.ExecuteInTransactionAsync(
            async token => await PostAsync(adjustment, decisionNotes, selfApproved, token),
            cancellationToken);
    }

    public async Task<OperationResult> RejectAsync(
        long id,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure(
                "Say why it is being turned down. A rejection with no reason tells the next person "
                + "nothing.",
                nameof(reason));
        }

        var adjustment = await LoadForDecisionAsync(id, cancellationToken);

        if (adjustment is null)
        {
            return OperationResult.Failure("That adjustment no longer exists.");
        }

        if (adjustment.Status != StockAdjustmentStatus.PendingApproval)
        {
            return OperationResult.Failure(
                $"{adjustment.Number} is already {adjustment.Status.ToString().ToLowerInvariant()}.");
        }

        adjustment.Status = StockAdjustmentStatus.Rejected;
        adjustment.DecisionNotes = reason.Trim();
        adjustment.ApprovedAtUtc = _clock.UtcNow;
        adjustment.ApprovedByUserId = _currentUser.UserId;

        await _audit.LogAsync(
            AuditActions.StockAdjustmentRejected,
            nameof(StockAdjustment),
            adjustment.Id.ToString(CultureInfo.InvariantCulture),
            $"Rejected {adjustment.Number}: {reason.Trim()}",
            new { adjustment.Number, adjustment.Reason, Decision = reason.Trim() },
            adjustment.BranchId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // -----------------------------------------------------------------------
    // Validation - no writes
    // -----------------------------------------------------------------------

    private async Task<OperationResult<AdjustmentPlan>> PrepareAsync(
        SubmitAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Lines.Count == 0)
        {
            return Fail("Add at least one line - an adjustment with nothing in it moves no stock.");
        }

        if (string.IsNullOrWhiteSpace(request.ReasonNotes))
        {
            return Fail(
                "Explain what happened. This is the one document type with no invoice behind it, so "
                + "the note is the whole record.",
                nameof(SubmitAdjustmentRequest.ReasonNotes));
        }

        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == request.WarehouseId && w.IsActive, cancellationToken);

        if (warehouse is null)
        {
            return Fail("Choose an active warehouse.", nameof(SubmitAdjustmentRequest.WarehouseId));
        }

        if (!await _db.Branches.AnyAsync(b => b.Id == request.BranchId && b.IsActive, cancellationToken))
        {
            return Fail("Choose an active branch.", nameof(SubmitAdjustmentRequest.BranchId));
        }

        if (!_currentUser.IsOwner && !_currentUser.CanAccessBranch(request.BranchId))
        {
            return Fail("You cannot adjust stock for that branch.", nameof(SubmitAdjustmentRequest.BranchId));
        }

        var today = _clock.ToBusinessDate(_clock.UtcNow);
        var date = request.AdjustmentDate ?? today;

        if (date > today)
        {
            return Fail(
                "An adjustment cannot be dated in the future - the stock has either changed or it has "
                + "not.",
                nameof(SubmitAdjustmentRequest.AdjustmentDate));
        }

        var plan = new AdjustmentPlan
        {
            WarehouseId = warehouse.Id,
            BranchId = request.BranchId,
            AdjustmentDate = date,
            Reason = request.Reason,
            ReasonNotes = request.ReasonNotes.Trim(),
        };

        var batchIds = request.Lines.Select(l => l.StockBatchId).Distinct().ToList();

        var batches = await _db.StockBatches
            .AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .Select(b => new
            {
                b.Id,
                b.ProductVariantId,
                b.BatchNumber,
                b.IsAutoGenerated,
                b.LandedUnitCost,
                Sku = b.ProductVariant!.Sku,
            })
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var seen = new HashSet<long>();

        foreach (var input in request.Lines)
        {
            if (!batches.TryGetValue(input.StockBatchId, out var batch))
            {
                return Fail(
                    "One of the batches on this adjustment no longer exists. Reload the screen and "
                    + "pick it again.");
            }

            // The variant is posted alongside the batch for the UI's benefit;
            // if the two disagree, the form has been tampered with or is stale,
            // and posting either one would be a guess.
            if (batch.ProductVariantId != input.ProductVariantId)
            {
                return Fail(
                    $"Batch {batch.BatchNumber} does not belong to {batch.Sku}. Reload the screen.");
            }

            if (input.QuantityChange == 0m)
            {
                return Fail($"Enter a quantity for {batch.Sku} - zero moves nothing.");
            }

            if (StockAdjustment.IsOutboundOnly(plan.Reason) && input.QuantityChange > 0m)
            {
                return Fail(
                    $"{plan.Reason} only takes stock out, so {batch.Sku} cannot be a positive "
                    + "quantity. Use \"Found extra\" or \"Correction\" to bring stock in.");
            }

            if (StockAdjustment.IsInboundOnly(plan.Reason) && input.QuantityChange < 0m)
            {
                return Fail(
                    $"{plan.Reason} only brings stock in, so {batch.Sku} cannot be a negative "
                    + "quantity.");
            }

            if (!seen.Add(input.StockBatchId))
            {
                return Fail(
                    $"Batch {batch.BatchNumber} appears twice. Combine those lines into one - two "
                    + "movements against the same batch have to be read together to mean anything.");
            }

            plan.Lines.Add(new PlannedAdjustmentLine
            {
                ProductVariantId = input.ProductVariantId,
                StockBatchId = input.StockBatchId,
                Sku = batch.Sku,
                BatchNumber = batch.BatchNumber,
                QuantityChange = input.QuantityChange,
                UnitCost = batch.LandedUnitCost,
                Notes = Trim(input.Notes),
            });
        }

        var shortfall = await FindShortfallAsync(
            plan.WarehouseId,
            plan.Lines.Select(l => (l.StockBatchId, l.ProductVariantId, l.QuantityChange, l.Sku, l.BatchNumber)),
            cancellationToken);

        if (shortfall is not null)
        {
            return Fail(shortfall);
        }

        return OperationResult<AdjustmentPlan>.Success(plan);
    }

    /// <summary>
    /// Refuses to take out more than is there.
    ///
    /// Negative stock is a branch policy elsewhere in this system - a sale can
    /// be allowed to run a balance below zero when the goods are known to be on
    /// their way. An adjustment is different: it claims to describe stock
    /// somebody has physically looked at, and you cannot damage twelve units of
    /// something when eight exist. That is a miscount, and the right answer is
    /// to say so rather than to record it.
    /// </summary>
    private async Task<string?> FindShortfallAsync(
        long warehouseId,
        IEnumerable<(long BatchId, long VariantId, decimal QuantityChange, string Sku, string BatchNumber)> lines,
        CancellationToken cancellationToken)
    {
        var outbound = lines.Where(l => l.QuantityChange < 0m).ToList();

        if (outbound.Count == 0)
        {
            return null;
        }

        var batchIds = outbound.Select(l => l.BatchId).ToList();

        var onHand = await _db.StockBalances
            .AsNoTracking()
            .Where(b => b.WarehouseId == warehouseId && batchIds.Contains(b.StockBatchId))
            .ToDictionaryAsync(b => b.StockBatchId, b => b.QuantityOnHand, cancellationToken);

        foreach (var line in outbound)
        {
            var available = onHand.GetValueOrDefault(line.BatchId, 0m);
            var wanted = -line.QuantityChange;

            if (wanted > available)
            {
                return $"There are only {available:N0} of {line.Sku} in batch {line.BatchNumber}, "
                       + $"and this takes out {wanted:N0}. Count it again - an adjustment describes "
                       + "stock somebody has looked at, so it cannot remove more than exists.";
            }
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Writing - always inside a transaction
    // -----------------------------------------------------------------------

    private async Task<StockAdjustment> CreateAsync(
        AdjustmentPlan plan,
        CancellationToken cancellationToken)
    {
        var adjustment = new StockAdjustment
        {
            Number = await NextNumberAsync(plan.AdjustmentDate, cancellationToken),
            WarehouseId = plan.WarehouseId,
            BranchId = plan.BranchId,
            AdjustmentDate = plan.AdjustmentDate,
            Reason = plan.Reason,
            ReasonNotes = plan.ReasonNotes,
            Status = StockAdjustmentStatus.PendingApproval,
            RequestedByUserId = _currentUser.UserId,
        };

        _db.StockAdjustments.Add(adjustment);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var line in plan.Lines)
        {
            // Added through the navigation rather than the DbSet, so that
            // Lines is populated for the caller. PostAsync runs straight after
            // this on the self-approval path and reads that collection.
            adjustment.Lines.Add(new StockAdjustmentLine
            {
                StockAdjustmentId = adjustment.Id,
                ProductVariantId = line.ProductVariantId,
                StockBatchId = line.StockBatchId,
                QuantityChange = line.QuantityChange,

                // Cost is left at zero until posting. A pending document has no
                // value, because the batch it points at can be recosted, sold
                // down or written off before anybody approves it.
                UnitCost = 0m,
                ValueChange = 0m,
                Notes = line.Notes,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return adjustment;
    }

    /// <summary>
    /// Turns an approved adjustment into stock movements.
    ///
    /// Availability is re-checked here even when it was checked at submission,
    /// because a pending document can sit for a day while the stock it names is
    /// sold. The check at submission is a courtesy; this one is the rule.
    /// </summary>
    private async Task<OperationResult> PostAsync(
        StockAdjustment adjustment,
        string? decisionNotes,
        bool selfApproved,
        CancellationToken cancellationToken)
    {
        var lines = adjustment.Lines.ToList();

        if (lines.Count == 0)
        {
            return OperationResult.Failure(
                $"{adjustment.Number} has no lines and cannot be posted.");
        }

        var batchIds = lines.Select(l => l.StockBatchId).ToList();

        var batches = await _db.StockBatches
            .Where(b => batchIds.Contains(b.Id))
            .Select(b => new { b.Id, b.BatchNumber, b.LandedUnitCost, Sku = b.ProductVariant!.Sku })
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        if (batches.Count != batchIds.Distinct().Count())
        {
            return OperationResult.Failure(
                $"A batch on {adjustment.Number} no longer exists, so it cannot be posted.");
        }

        var shortfall = await FindShortfallAsync(
            adjustment.WarehouseId,
            lines.Select(l => (
                l.StockBatchId,
                l.ProductVariantId,
                l.QuantityChange,
                batches[l.StockBatchId].Sku,
                batches[l.StockBatchId].BatchNumber)),
            cancellationToken);

        if (shortfall is not null)
        {
            return OperationResult.Failure(
                $"{shortfall} (Stock has moved since this adjustment was written.)");
        }

        var now = _clock.UtcNow;

        foreach (var line in lines)
        {
            var batch = batches[line.StockBatchId];

            // Cost is frozen onto the line at posting, from the batch as it
            // stands now. That is the figure the ledger records and the one this
            // loss is worth, whatever happens to the batch afterwards.
            line.UnitCost = batch.LandedUnitCost;
            line.ValueChange = decimal.Round(
                line.QuantityChange * batch.LandedUnitCost, 4, MidpointRounding.AwayFromZero);

            await _movements.AppendAsync(
                new StockMovement
                {
                    ProductVariantId = line.ProductVariantId,
                    WarehouseId = adjustment.WarehouseId,
                    StockBatchId = line.StockBatchId,
                    MovementType = StockAdjustment.MovementFor(adjustment.Reason, line.QuantityChange),
                    QuantityChange = line.QuantityChange,
                    UnitCost = batch.LandedUnitCost,
                    BranchId = adjustment.BranchId,
                    DocumentType = StockDocumentType.StockAdjustment,
                    DocumentId = adjustment.Id,
                    DocumentNumber = adjustment.Number,
                    OccurredAtUtc = now,
                    BusinessDate = adjustment.AdjustmentDate,
                    Notes = line.Notes ?? adjustment.ReasonNotes,
                },
                cancellationToken);
        }

        adjustment.Status = StockAdjustmentStatus.Posted;
        adjustment.ApprovedAtUtc = now;
        adjustment.ApprovedByUserId = _currentUser.UserId;
        adjustment.WasSelfApproved = selfApproved;
        adjustment.PostedAtUtc = now;
        adjustment.DecisionNotes = Trim(decisionNotes);

        var netUnits = lines.Sum(l => l.QuantityChange);
        var netValue = lines.Sum(l => l.ValueChange);

        await _audit.LogAsync(
            AuditActions.StockAdjustmentPosted,
            nameof(StockAdjustment),
            adjustment.Id.ToString(CultureInfo.InvariantCulture),
            $"Posted {adjustment.Number} ({adjustment.Reason}): {netUnits:N0} unit(s), "
            + $"{netValue:N2} BDT. {adjustment.ReasonNotes}",
            new
            {
                adjustment.Number,
                adjustment.Reason,
                adjustment.ReasonNotes,
                SelfApproved = selfApproved,
                NetUnits = netUnits,
                NetValue = netValue,
                Lines = lines.Select(l => new
                {
                    batches[l.StockBatchId].Sku,
                    Batch = batches[l.StockBatchId].BatchNumber,
                    l.QuantityChange,
                    l.UnitCost,
                    l.ValueChange,
                }),
            },
            adjustment.BranchId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private Task<StockAdjustment?> LoadForDecisionAsync(long id, CancellationToken cancellationToken) =>
        _db.StockAdjustments
            .Include(a => a.Lines)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    private async Task<string> NextNumberAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var prefix = DocumentNumber.Prefix(DocumentNumber.StockAdjustment, date);

        var used = await _db.StockAdjustments
            .Where(a => a.Number.StartsWith(prefix))
            .Select(a => a.Number)
            .ToListAsync(cancellationToken);

        return DocumentNumber.Next(prefix, used);
    }

    private static OperationResult<AdjustmentPlan> Fail(string error, string? field = null) =>
        OperationResult<AdjustmentPlan>.Failure(error, field);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // -----------------------------------------------------------------------
    // Everything decided before the first write
    // -----------------------------------------------------------------------

    private sealed class AdjustmentPlan
    {
        public required long WarehouseId { get; init; }

        public required long BranchId { get; init; }

        public required DateOnly AdjustmentDate { get; init; }

        public required StockAdjustmentReason Reason { get; init; }

        public required string ReasonNotes { get; init; }

        public List<PlannedAdjustmentLine> Lines { get; } = [];
    }

    private sealed class PlannedAdjustmentLine
    {
        public required long ProductVariantId { get; init; }

        public required long StockBatchId { get; init; }

        public required string Sku { get; init; }

        public required string BatchNumber { get; init; }

        public required decimal QuantityChange { get; init; }

        public required decimal UnitCost { get; init; }

        public string? Notes { get; init; }
    }
}

/// <summary>
/// What submitting produced. <see cref="WasPosted"/> is the difference between
/// "stock has moved" and "somebody has to look at this", and the screen says
/// something different for each.
/// </summary>
public class AdjustmentSubmission
{
    public long Id { get; set; }

    public bool WasPosted { get; set; }
}
