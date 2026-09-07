using System.Globalization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Finance;
using EchoLifestyle.Application.Inventory;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Sales.Orders;

/// <summary>
/// Moving an order through its life.
///
/// Two moments matter, and they are deliberately different:
///
/// <b>Confirming</b> reserves stock. Nothing moves - the jar is still on the
/// shelf - but it stops being sellable to anybody else. No ledger entry is
/// written, because the ledger records movements and nothing has moved.
///
/// <b>Dispatching</b> is when stock actually leaves. The reservation is
/// released and an Issue entry is written for each batch that went, at that
/// batch's own cost. Only here does the order learn what it cost to fulfil.
///
/// Everything between and after - packing, delivery, refusal - is bookkeeping
/// around those two events.
/// </summary>
public partial class SalesOrderService
{
    /// <summary>
    /// The customer said yes. Stock is promised.
    ///
    /// This is where availability is enforced, not at draft. Two orders cannot
    /// be promised the same unit: the second sees the first one's claim and is
    /// refused, naming the product that is short.
    /// </summary>
    public async Task<OperationResult> ConfirmAsync(
        long id,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(id, cancellationToken);

        if (order is null)
        {
            return OperationResult.Failure("That order no longer exists.");
        }

        if (order.Status != SalesOrderStatus.Draft)
        {
            return OperationResult.Failure(
                $"{order.Number} is already {Describe(order.Status)}.");
        }

        if (order.Lines.Count == 0)
        {
            return OperationResult.Failure($"{order.Number} has no lines to confirm.");
        }

        // Re-checked at confirmation: a customer can be blocked between the
        // draft being written and somebody getting round to confirming it, and
        // this is the moment stock stops being sellable to anybody else.
        var blocked = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == order.CustomerId && c.IsBlocked)
            .Select(c => new { c.FullName, c.BlockReason })
            .FirstOrDefaultAsync(cancellationToken);

        if (blocked is not null)
        {
            return OperationResult.Failure(
                $"{blocked.FullName} was blocked after this order was written: {blocked.BlockReason}");
        }

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                // Planned in full before anything is touched. An order reserves
                // every line or none of them, and abandoning a loop halfway
                // would leave modified balances sitting on the context for a
                // later save to flush.
                var planned = await _reservations.PlanAsync(order, token);

                if (!planned.Succeeded)
                {
                    return OperationResult.Failure(planned.Error!);
                }

                await _reservations.ApplyAsync(order, planned.Value!, token);

                Advance(order, SalesOrderStatus.Confirmed, note);

                await _audit.LogAsync(
                    AuditActions.SalesOrderConfirmed,
                    nameof(SalesOrder),
                    order.Id.ToString(CultureInfo.InvariantCulture),
                    $"Confirmed {order.Number}: {order.Lines.Count} line(s), "
                    + $"{order.GrandTotal:N2} BDT. Stock reserved, nothing shipped.",
                    new
                    {
                        order.Number,
                        order.GrandTotal,
                        Reserved = order.Reservations
                            .Select(r => new { r.StockBatchId, r.Quantity }),
                    },
                    order.BranchId,
                    token);

                await _db.SaveChangesAsync(token);

                return OperationResult.Success();
            },
            cancellationToken);
    }

    /// <summary>
    /// In a box, waiting for the courier. Nothing moves and nothing is released -
    /// this is a status so the person packing and the person dispatching can
    /// tell their work apart.
    /// </summary>
    public async Task<OperationResult> MarkPackedAsync(
        long id,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(id, cancellationToken);

        if (order is null)
        {
            return OperationResult.Failure("That order no longer exists.");
        }

        if (order.Status != SalesOrderStatus.Confirmed)
        {
            return OperationResult.Failure(
                $"{order.Number} is {Describe(order.Status)}. Only a confirmed order can be packed.");
        }

        Advance(order, SalesOrderStatus.Packed, note);
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Handed to the courier. This is where stock actually leaves.
    ///
    /// The reservation is released and the same quantities are issued for real,
    /// first expired first out against what is on the shelf now. Allocating
    /// afresh rather than trusting the reservation is deliberate: a batch can be
    /// written off or counted away between confirmation and dispatch, and the
    /// truth is what is there when the parcel is packed.
    /// </summary>
    public async Task<OperationResult> DispatchAsync(
        DispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return OperationResult.Failure("That order no longer exists.");
        }

        if (order.Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.Packed))
        {
            return OperationResult.Failure(
                $"{order.Number} is {Describe(order.Status)} and cannot be dispatched.");
        }

        if (string.IsNullOrWhiteSpace(request.CourierName))
        {
            return OperationResult.Failure(
                "Name the courier. Chasing a parcel starts with knowing who has it.",
                nameof(DispatchRequest.CourierName));
        }

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                var now = _clock.UtcNow;
                var businessDate = _clock.ToBusinessDate(now);

                // Every line is allocated before a single movement is written.
                // Allocating afresh rather than trusting the reservation is
                // deliberate - a batch can be written off or counted away
                // between confirmation and dispatch - but a shortfall on line
                // three must not leave lines one and two already shipped.
                //
                // This order's own reservations count as available: it is about
                // to release them, and must not be told it is competing with
                // itself.
                var planned = new List<(SalesOrderLine Line, FefoAllocation.Result Allocation)>();
                var pool = new Dictionary<long, List<FefoAllocation.Candidate>>();

                foreach (var line in order.Lines)
                {
                    if (!pool.TryGetValue(line.ProductVariantId, out var candidates))
                    {
                        candidates = await _reservations.CandidatesAsync(
                            line.ProductVariantId, order.WarehouseId, token, order.Id);

                        pool[line.ProductVariantId] = candidates;
                    }

                    var allocation = FefoAllocation.Allocate(candidates, line.Quantity);

                    if (!allocation.IsComplete)
                    {
                        return OperationResult.Failure(
                            $"Only {candidates.Sum(c => c.Available):N0} of {line.Sku} is on the "
                            + $"shelf and this order needs {line.Quantity:N0}. Stock has moved since "
                            + "the order was confirmed - count it before shipping.");
                    }

                    planned.Add((line, allocation));

                    var taken = allocation.Takes
                        .GroupBy(t => t.StockBatchId)
                        .ToDictionary(g => g.Key, g => g.Sum(t => t.Quantity));

                    pool[line.ProductVariantId] = candidates
                        .Select(c => new FefoAllocation.Candidate
                        {
                            StockBatchId = c.StockBatchId,
                            Available = c.Available - taken.GetValueOrDefault(c.StockBatchId, 0m),
                            ExpiryDate = c.ExpiryDate,
                            ReceivedDate = c.ReceivedDate,
                            LandedUnitCost = c.LandedUnitCost,
                        })
                        .Where(c => c.Available > 0m)
                        .ToList();
                }

                // Sound. Now the stock actually leaves.
                await _reservations.ReleaseAllAsync(order, token);

                var totalCost = 0m;

                foreach (var (line, allocation) in planned)
                {
                    foreach (var take in allocation.Takes)
                    {
                        await _movements.AppendAsync(
                            new StockMovement
                            {
                                ProductVariantId = line.ProductVariantId,
                                WarehouseId = order.WarehouseId,
                                StockBatchId = take.StockBatchId,
                                MovementType = StockMovementType.Issue,
                                QuantityChange = -take.Quantity,
                                UnitCost = take.LandedUnitCost,
                                BranchId = order.BranchId,
                                DocumentType = StockDocumentType.SalesOrder,
                                DocumentId = order.Id,
                                DocumentNumber = order.Number,
                                OccurredAtUtc = now,
                                BusinessDate = businessDate,
                            },
                            token);
                    }

                    // Cost is frozen onto the line here, from the batches that
                    // actually went. Margin only means something from this
                    // moment on.
                    line.CostOfGoods = allocation.TotalCost;
                    totalCost += allocation.TotalCost;
                }

                order.CostOfGoods = totalCost;
                order.CourierName = request.CourierName.Trim();
                order.ConsignmentNumber = Trim(request.ConsignmentNumber);
                order.DispatchedAtUtc = now;

                Advance(order, SalesOrderStatus.Dispatched, request.Note);

                await _audit.LogAsync(
                    AuditActions.SalesOrderDispatched,
                    nameof(SalesOrder),
                    order.Id.ToString(CultureInfo.InvariantCulture),
                    $"Dispatched {order.Number} via {order.CourierName}"
                    + (order.ConsignmentNumber is null ? "" : $" ({order.ConsignmentNumber})")
                    + $". {order.GrandTotal:N2} BDT to collect.",
                    new
                    {
                        order.Number,
                        order.CourierName,
                        order.ConsignmentNumber,
                        order.GrandTotal,
                        order.CostOfGoods,
                    },
                    order.BranchId,
                    token);

                await _db.SaveChangesAsync(token);

                return OperationResult.Success();
            },
            cancellationToken);
    }

    /// <summary>
    /// The parcel arrived and, usually, the money came back.
    ///
    /// A short collection is recorded as it is rather than rounded up to the
    /// total. Couriers remit late, short, and net of their own fee, and the
    /// difference between what was owed and what arrived is the only thing that
    /// makes "how much is still out there" answerable.
    /// </summary>
    public async Task<OperationResult> MarkDeliveredAsync(
        DeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadAsync(request.OrderId, cancellationToken);

        if (order is null)
        {
            return OperationResult.Failure("That order no longer exists.");
        }

        if (order.Status != SalesOrderStatus.Dispatched)
        {
            return OperationResult.Failure(
                $"{order.Number} is {Describe(order.Status)}. Only a dispatched order can be "
                + "marked delivered.");
        }

        var collected = request.AmountCollected ?? order.GrandTotal;

        if (collected < 0m)
        {
            return OperationResult.Failure(
                "A negative collection is a refund, which is a different document.",
                nameof(DeliveryRequest.AmountCollected));
        }

        if (collected > order.GrandTotal)
        {
            return OperationResult.Failure(
                $"That is more than the {order.GrandTotal:N2} BDT owed on {order.Number}. "
                + "Check the figure - an overpayment is not recorded here.",
                nameof(DeliveryRequest.AmountCollected));
        }

        var now = _clock.UtcNow;

        return await _db.ExecuteInTransactionAsync(
            async token => await RecordDeliveryAsync(
                order, collected, request.Note, reference: null, now, token),
            cancellationToken);
    }

    /// <summary>
    /// Records a delivery and whatever came with it.
    ///
    /// Shared with the courier remittance, which settles forty of these at once
    /// and must produce identical rows. Assumes it is already inside a
    /// transaction - the caller owns that, because a statement posts in full or
    /// not at all.
    /// </summary>
    internal async Task<OperationResult> RecordDeliveryAsync(
        SalesOrder order,
        decimal collected,
        string? note,
        string? reference,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (collected > 0m)
        {
            // Money is recorded as a transaction, never as a number typed over
            // the collected column. That column is a projection of these rows,
            // which is what makes "why does this say it is paid?" answerable.
            await _cash.AppendAsync(
                CashEntry.ForOrder(
                    order,
                    collected,
                    MethodFor(order.PaymentMethod),
                    _clock.ToBusinessDate(now),
                    reference,
                    notes: note),
                cancellationToken);
        }

        order.DeliveredAtUtc = now;

        Advance(order, SalesOrderStatus.Delivered, note);

        await _audit.LogAsync(
            AuditActions.SalesOrderDelivered,
            nameof(SalesOrder),
            order.Id.ToString(CultureInfo.InvariantCulture),
            $"Delivered {order.Number}. Collected {collected:N2} of {order.GrandTotal:N2} BDT"
            + (order.AmountOutstanding > 0m
                ? $" - {order.AmountOutstanding:N2} still owed."
                : "."),
            new
            {
                order.Number,
                order.GrandTotal,
                Collected = collected,
                Outstanding = order.AmountOutstanding,
            },
            order.BranchId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// How the money for an order most likely arrived, from what the customer
    /// agreed to. A courier remittance overrides this with what actually
    /// happened - cash on delivery is very often settled by the courier's bKash
    /// transfer.
    /// </summary>
    private static PaymentMethodKind MethodFor(PaymentMethod method) => method switch
    {
        PaymentMethod.Bkash => PaymentMethodKind.Bkash,
        PaymentMethod.Nagad => PaymentMethodKind.Nagad,
        PaymentMethod.Card => PaymentMethodKind.Card,
        PaymentMethod.BankTransfer => PaymentMethodKind.BankTransfer,
        _ => PaymentMethodKind.Cash,
    };

    /// <summary>
    /// The parcel came back.
    ///
    /// The ordinary tax of selling cash on delivery: the customer refused it,
    /// was unreachable, or changed their mind at the door. Stock returns to the
    /// shelf as a real movement, into the batches it left from where they still
    /// exist.
    /// </summary>
    public async Task<OperationResult> MarkReturnedAsync(
        long id,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure(
                "Say why it came back. Refusal reasons are the only way to see a pattern before it "
                + "gets expensive.",
                nameof(reason));
        }

        var order = await LoadAsync(id, cancellationToken);

        if (order is null)
        {
            return OperationResult.Failure("That order no longer exists.");
        }

        if (order.Status != SalesOrderStatus.Dispatched)
        {
            return OperationResult.Failure(
                $"{order.Number} is {Describe(order.Status)}. Only a dispatched order can come back.");
        }

        return await _db.ExecuteInTransactionAsync(
            async token => await RecordReturnAsync(order, reason.Trim(), token),
            cancellationToken);
    }

    /// <summary>
    /// Puts a returned parcel back on the shelf.
    ///
    /// Shared with the courier remittance, which reports returns on the same
    /// statement as collections. Assumes a transaction is already open - nesting
    /// one inside another is not something EF Core will do quietly.
    /// </summary>
    internal async Task<OperationResult> RecordReturnAsync(
        SalesOrder order,
        string reason,
        CancellationToken token)
    {
        var now = _clock.UtcNow;
        var businessDate = _clock.ToBusinessDate(now);

        // Reversed from the ledger, not from the lines. The entries say exactly
        // which batch each unit came out of, and putting stock back into a
        // different batch would quietly change what the remaining stock is
        // worth.
        var issued = await _db.StockLedger
            .AsNoTracking()
            .Where(e => e.DocumentType == StockDocumentType.SalesOrder
                        && e.DocumentId == order.Id
                        && e.MovementType == StockMovementType.Issue)
            .Select(e => new
            {
                e.ProductVariantId,
                e.StockBatchId,
                e.QuantityChange,
                e.UnitCost,
            })
            .ToListAsync(token);

        foreach (var entry in issued)
        {
            await _movements.AppendAsync(
                new StockMovement
                {
                    ProductVariantId = entry.ProductVariantId,
                    WarehouseId = order.WarehouseId,
                    StockBatchId = entry.StockBatchId,
                    MovementType = StockMovementType.ReturnFromCustomer,

                    // The issue was negative; the return is its mirror.
                    QuantityChange = -entry.QuantityChange,
                    UnitCost = entry.UnitCost,
                    BranchId = order.BranchId,
                    DocumentType = StockDocumentType.SalesOrder,
                    DocumentId = order.Id,
                    DocumentNumber = order.Number,
                    OccurredAtUtc = now,
                    BusinessDate = businessDate,
                    Notes = $"Returned: {reason}",
                },
                token);
        }

        order.ReturnReason = reason;
        order.ReturnedAtUtc = now;

        // Cost of goods goes back to zero: nothing was ultimately sold, so this
        // order contributed no margin and no cost.
        order.CostOfGoods = 0m;

        foreach (var line in order.Lines)
        {
            line.CostOfGoods = 0m;
        }

        Advance(order, SalesOrderStatus.Returned, reason);

        await _audit.LogAsync(
            AuditActions.SalesOrderReturned,
            nameof(SalesOrder),
            order.Id.ToString(CultureInfo.InvariantCulture),
            $"{order.Number} came back: {reason} {order.GrandTotal:N2} BDT not collected.",
            new
            {
                order.Number,
                Reason = reason,
                order.GrandTotal,
                order.CourierName,
                BatchesRestored = issued.Count,
            },
            order.BranchId,
            token);

        await _db.SaveChangesAsync(token);

        return OperationResult.Success();
    }

    /// <summary>
    /// Called off before it shipped.
    ///
    /// Only possible before dispatch. Once a parcel is with a courier the ending
    /// is delivered or returned - cancelling would leave stock that has
    /// physically gone still counted as on the shelf.
    /// </summary>
    public async Task<OperationResult> CancelAsync(
        long id,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult.Failure(
                "Say why it was cancelled.", nameof(reason));
        }

        var order = await LoadAsync(id, cancellationToken);

        if (order is null)
        {
            return OperationResult.Failure("That order no longer exists.");
        }

        if (SalesOrder.IsFinished(order.Status))
        {
            return OperationResult.Failure($"{order.Number} is already {Describe(order.Status)}.");
        }

        if (order.Status == SalesOrderStatus.Dispatched)
        {
            return OperationResult.Failure(
                $"{order.Number} is already with the courier. Mark it returned when the parcel comes "
                + "back - cancelling would leave stock that has physically gone still counted as "
                + "being here.");
        }

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                // Whatever was promised goes back on the shelf. No ledger entry:
                // nothing ever moved.
                await _reservations.ReleaseAllAsync(order, token);

                order.CancelReason = reason.Trim();
                order.CancelledAtUtc = _clock.UtcNow;
                order.CancelledByUserId = _currentUser.UserId;

                Advance(order, SalesOrderStatus.Cancelled, reason.Trim());

                await _audit.LogAsync(
                    AuditActions.SalesOrderCancelled,
                    nameof(SalesOrder),
                    order.Id.ToString(CultureInfo.InvariantCulture),
                    $"Cancelled {order.Number}: {reason.Trim()}",
                    new { order.Number, Reason = reason.Trim(), order.GrandTotal },
                    order.BranchId,
                    token);

                await _db.SaveChangesAsync(token);

                return OperationResult.Success();
            },
            cancellationToken);
    }

    /// <summary>
    /// Moves the status and writes the history row.
    ///
    /// One place, so that no transition can happen without leaving a trace of
    /// who moved it and when. "When did this ship?" and "who cancelled it?" are
    /// asked constantly, and a single status column answers neither.
    /// </summary>
    private void Advance(SalesOrder order, SalesOrderStatus to, string? note)
    {
        var from = order.Status;

        order.Status = to;

        order.StatusHistory.Add(new SalesOrderStatusChange
        {
            SalesOrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            OccurredAtUtc = _clock.UtcNow,
            UserId = _currentUser.UserId,
            Note = Trim(note),
        });
    }
}
