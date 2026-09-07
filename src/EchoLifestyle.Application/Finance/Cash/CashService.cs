using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Finance.Cash;

/// <summary>
/// Money recorded by hand: what the business spent, what it paid suppliers,
/// what the partners put in and took out, and what it gave back to customers.
///
/// Everything here goes through <c>CashTransactionWriter</c> into the same
/// append-only log a courier payout writes to. There is no expense document and
/// no separate expense table, deliberately: an expense that is paid when it is
/// incurred <em>is</em> a cash transaction, and a second table holding the same
/// facts is a second table to disagree with the first. Bills owed but not yet
/// paid are a different thing - accounts payable - and they arrive with
/// double-entry, not here.
///
/// Nothing is edited and nothing is deleted. A mistake is cancelled by a
/// reversing entry that keeps the original's kind, category and party, so the
/// category totals net to the right figure without any report knowing that
/// reversals exist.
/// </summary>
public partial class CashService
{
    /// <summary>
    /// Kinds somebody may write on this screen.
    ///
    /// A courier fee is absent because it belongs to a payout statement and is
    /// written when that posts; typing one here would double-count the cost of
    /// delivery. A transfer between the cash box and bKash is absent because it
    /// is two entries, not one, and moving money between pots is a Phase 6
    /// problem once there is a bank reconciliation to check it against.
    /// </summary>
    public static readonly IReadOnlyList<CashKind> RecordableKinds =
    [
        CashKind.Expense,
        CashKind.SupplierPayment,
        CashKind.CustomerRefund,
        CashKind.OrderCollection,
        CashKind.PartnerCapital,
        CashKind.PartnerDrawing,
        CashKind.Opening,
    ];

    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly CashTransactionWriter _cash;

    public CashService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        IAuditLogger audit,
        CashTransactionWriter cash)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _cash = cash;
    }

    /// <summary>
    /// Writes one entry to the money log.
    /// </summary>
    public async Task<OperationResult<long>> RecordAsync(
        RecordCashRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validated = await ValidateAsync(request, cancellationToken);

        if (!validated.Succeeded)
        {
            return OperationResult<long>.Failure(validated.Error!, validated.Field);
        }

        var plan = validated.Value!;

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                var transaction = await _cash.AppendAsync(
                    new CashEntry
                    {
                        Direction = plan.Direction,
                        Kind = request.Kind,
                        Method = request.Method,
                        Amount = plan.Amount,
                        TransactionDate = plan.Date,
                        BranchId = request.BranchId,
                        ReferenceNumber = request.ReferenceNumber,
                        ExpenseCategoryId = request.ExpenseCategoryId,
                        PartnerId = request.PartnerId,
                        SupplierId = request.SupplierId,
                        SalesOrderId = plan.Order?.Id,
                        CustomerId = plan.Order?.CustomerId,
                        Notes = request.Notes,
                    },
                    token);

                await _db.SaveChangesAsync(token);

                await _audit.LogAsync(
                    AuditActions.CashRecorded,
                    nameof(CashTransaction),
                    transaction.Id.ToString(),
                    $"{Describe(request.Kind)} {plan.Amount:N2} "
                    + $"({plan.Direction.ToString().ToLowerInvariant()}) by {request.Method}",
                    new
                    {
                        transaction.Number,
                        Kind = request.Kind.ToString(),
                        Direction = plan.Direction.ToString(),
                        Method = request.Method.ToString(),
                        plan.Amount,
                        Date = plan.Date,
                        request.ExpenseCategoryId,
                        request.PartnerId,
                        request.SupplierId,
                        Order = plan.Order?.Number,
                        request.ReferenceNumber,
                    },
                    request.BranchId,
                    token);

                return OperationResult<long>.Success(transaction.Id);
            },
            cancellationToken);
    }

    /// <summary>
    /// Cancels an entry with an equal and opposite one.
    ///
    /// The only way back out of an append-only log. The reversal carries the
    /// original's kind, category, partner, supplier and order, so an order's
    /// collected figure, a category's total and a partner's balance all correct
    /// themselves - and the original stays visible, which is the point.
    /// </summary>
    public async Task<OperationResult<long>> ReverseAsync(
        long id,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationResult<long>.Failure(
                "Say why this is being reversed. It is the only record of what went wrong.",
                nameof(reason));
        }

        var original = await _db.CashTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (original is null)
        {
            return OperationResult<long>.Failure("That entry no longer exists.");
        }

        if (original.ReversesCashTransactionId is not null)
        {
            return OperationResult<long>.Failure(
                $"{original.Number} is itself a reversal. Reversing a reversal to reinstate the "
                + "original hides what happened - record the entry again instead.");
        }

        var alreadyReversed = await _db.CashTransactions
            .AnyAsync(t => t.ReversesCashTransactionId == id, cancellationToken);

        if (alreadyReversed)
        {
            return OperationResult<long>.Failure($"{original.Number} has already been reversed.");
        }

        if (!_currentUser.IsOwner && !_currentUser.CanAccessBranch(original.BranchId))
        {
            return OperationResult<long>.Failure("That entry belongs to another branch.");
        }

        var flipped = original.Direction == CashDirection.In
            ? CashDirection.Out
            : CashDirection.In;

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                var reversal = await _cash.AppendAsync(
                    new CashEntry
                    {
                        Direction = flipped,
                        Kind = original.Kind,
                        Method = original.Method,
                        Amount = original.Amount,

                        // Dated today, not backdated to the original. The money
                        // was wrong from the original's date until now, and a
                        // backdated reversal would rewrite a period somebody has
                        // already looked at and reported on.
                        TransactionDate = _clock.ToBusinessDate(_clock.UtcNow),
                        BranchId = original.BranchId,
                        ReferenceNumber = original.ReferenceNumber,
                        ExpenseCategoryId = original.ExpenseCategoryId,
                        PartnerId = original.PartnerId,
                        SupplierId = original.SupplierId,
                        SalesOrderId = original.SalesOrderId,
                        CustomerId = original.CustomerId,
                        CourierRemittanceId = original.CourierRemittanceId,
                        ReversesCashTransactionId = original.Id,
                        Notes = $"Reverses {original.Number}: {reason.Trim()}",
                    },
                    token);

                await _db.SaveChangesAsync(token);

                await _audit.LogAsync(
                    AuditActions.CashReversed,
                    nameof(CashTransaction),
                    original.Id.ToString(),
                    $"{original.Number} reversed by {reversal.Number}: {reason.Trim()}",
                    new
                    {
                        Original = original.Number,
                        Reversal = reversal.Number,
                        Kind = original.Kind.ToString(),
                        original.Amount,
                        Reason = reason.Trim(),
                        FromCourierPayout = original.CourierRemittanceId is not null,
                    },
                    original.BranchId,
                    token);

                return OperationResult<long>.Success(reversal.Id);
            },
            cancellationToken);
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Everything decided before anything is written: the direction the kind
    /// implies, the rounded amount, the date, and the order when there is one.
    /// </summary>
    private sealed record RecordPlan(
        CashDirection Direction,
        decimal Amount,
        DateOnly Date,
        SalesOrder? Order);

    private async Task<OperationResult<RecordPlan>> ValidateAsync(
        RecordCashRequest request,
        CancellationToken cancellationToken)
    {
        if (!RecordableKinds.Contains(request.Kind))
        {
            return OperationResult<RecordPlan>.Failure(
                request.Kind == CashKind.CourierFee
                    ? "Courier charges are recorded when a payout is posted, not by hand - "
                      + "entering one here would count the cost of delivery twice."
                    : $"{Describe(request.Kind)} cannot be recorded from this screen.",
                nameof(RecordCashRequest.Kind));
        }

        var direction = CashTransaction.NaturalDirection(request.Kind);

        if (direction is null)
        {
            // Unreachable while RecordableKinds holds only one-way kinds. Kept
            // so that adding Transfer to that list fails loudly rather than
            // writing a row with an arbitrary direction.
            return OperationResult<RecordPlan>.Failure(
                $"{Describe(request.Kind)} does not have a single direction and needs its own "
                + "screen.",
                nameof(RecordCashRequest.Kind));
        }

        var amount = decimal.Round(request.Amount, 4, MidpointRounding.AwayFromZero);

        if (amount <= 0m)
        {
            return OperationResult<RecordPlan>.Failure(
                "How much was it?", nameof(RecordCashRequest.Amount));
        }

        var today = _clock.ToBusinessDate(_clock.UtcNow);
        var date = request.TransactionDate ?? today;

        if (date > today)
        {
            return OperationResult<RecordPlan>.Failure(
                "Money cannot have moved in the future.",
                nameof(RecordCashRequest.TransactionDate));
        }

        var branch = await _db.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.BranchId && b.IsActive, cancellationToken);

        if (branch is null)
        {
            return OperationResult<RecordPlan>.Failure(
                "Choose an active branch.", nameof(RecordCashRequest.BranchId));
        }

        if (!_currentUser.IsOwner && !_currentUser.CanAccessBranch(request.BranchId))
        {
            return OperationResult<RecordPlan>.Failure(
                "You cannot record money against that branch.",
                nameof(RecordCashRequest.BranchId));
        }

        if (CashTransaction.RequiresExpenseCategory(request.Kind))
        {
            var categoryOk = request.ExpenseCategoryId is not null
                && await _db.ExpenseCategories.AnyAsync(
                    c => c.Id == request.ExpenseCategoryId && c.IsActive, cancellationToken);

            if (!categoryOk)
            {
                return OperationResult<RecordPlan>.Failure(
                    "What was the money spent on? An uncategorised expense is a number nobody "
                    + "can act on later.",
                    nameof(RecordCashRequest.ExpenseCategoryId));
            }
        }
        else if (request.ExpenseCategoryId is not null)
        {
            return OperationResult<RecordPlan>.Failure(
                "Only an expense carries a category.",
                nameof(RecordCashRequest.ExpenseCategoryId));
        }

        if (CashTransaction.RequiresPartner(request.Kind))
        {
            if (!_currentUser.IsOwner
                && !_currentUser.HasPermission(Permissions.Finance.PartnerLedgerView))
            {
                return OperationResult<RecordPlan>.Failure(
                    "Partner capital is not yours to record.",
                    nameof(RecordCashRequest.PartnerId));
            }

            var partnerOk = request.PartnerId is not null
                && await _db.Partners.AnyAsync(
                    p => p.Id == request.PartnerId && p.IsActive, cancellationToken);

            if (!partnerOk)
            {
                return OperationResult<RecordPlan>.Failure(
                    "Whose money was it?", nameof(RecordCashRequest.PartnerId));
            }
        }
        else if (request.PartnerId is not null)
        {
            return OperationResult<RecordPlan>.Failure(
                "Only capital and drawings name a partner.",
                nameof(RecordCashRequest.PartnerId));
        }

        if (CashTransaction.RequiresSupplier(request.Kind))
        {
            var supplierOk = request.SupplierId is not null
                && await _db.Suppliers.AnyAsync(
                    s => s.Id == request.SupplierId, cancellationToken);

            if (!supplierOk)
            {
                return OperationResult<RecordPlan>.Failure(
                    "Which supplier was paid?", nameof(RecordCashRequest.SupplierId));
            }
        }

        SalesOrder? order = null;

        if (CashTransaction.RequiresSalesOrder(request.Kind))
        {
            var planned = await ResolveOrderAsync(request, amount, cancellationToken);

            if (!planned.Succeeded)
            {
                return OperationResult<RecordPlan>.Failure(planned.Error!, planned.Field);
            }

            order = planned.Value;
        }
        else if (!string.IsNullOrWhiteSpace(request.OrderNumber))
        {
            return OperationResult<RecordPlan>.Failure(
                "Only a collection or a refund attaches to an order.",
                nameof(RecordCashRequest.OrderNumber));
        }

        return OperationResult<RecordPlan>.Success(
            new RecordPlan(direction.Value, amount, date, order));
    }

    /// <summary>
    /// Finds the order by number and checks the amount is possible against it.
    /// </summary>
    private async Task<OperationResult<SalesOrder>> ResolveOrderAsync(
        RecordCashRequest request,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrderNumber))
        {
            return OperationResult<SalesOrder>.Failure(
                "Which order is this for?", nameof(RecordCashRequest.OrderNumber));
        }

        var number = request.OrderNumber.Trim();

        var order = await _db.SalesOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Number == number, cancellationToken);

        if (order is null)
        {
            return OperationResult<SalesOrder>.Failure(
                $"No order numbered {number}.", nameof(RecordCashRequest.OrderNumber));
        }

        if (order.Status is SalesOrderStatus.Draft or SalesOrderStatus.Cancelled)
        {
            return OperationResult<SalesOrder>.Failure(
                $"{order.Number} is {order.Status.ToString().ToLowerInvariant()}. Money against it "
                + "would have nothing to settle.",
                nameof(RecordCashRequest.OrderNumber));
        }

        // The truth, summed from the log, rather than the projection on the
        // order. They agree; this is the one place it is worth being certain,
        // because the answer decides whether money is refused.
        var collected = await _cash.CollectedForOrderAsync(order.Id, cancellationToken);

        if (request.Kind == CashKind.OrderCollection)
        {
            var outstanding = order.GrandTotal - collected;

            if (amount > outstanding)
            {
                return outstanding <= 0m
                    ? OperationResult<SalesOrder>.Failure(
                        $"{order.Number} is already paid in full.",
                        nameof(RecordCashRequest.Amount))
                    : OperationResult<SalesOrder>.Failure(
                        $"{order.Number} only has {outstanding:N2} outstanding. Taking more than "
                        + "the order is worth needs a credit note, not a collection.",
                        nameof(RecordCashRequest.Amount));
            }
        }
        else if (amount > collected)
        {
            return OperationResult<SalesOrder>.Failure(
                collected <= 0m
                    ? $"Nothing has been collected against {order.Number}, so there is nothing to "
                      + "refund."
                    : $"Only {collected:N2} was collected against {order.Number}. Refunding more "
                      + "than came in would be a payment, not a refund.",
                nameof(RecordCashRequest.Amount));
        }

        return OperationResult<SalesOrder>.Success(order);
    }

    /// <summary>A kind in words, for a screen or an audit line.</summary>
    public static string Describe(CashKind kind) => kind switch
    {
        CashKind.OrderCollection => "Order collection",
        CashKind.CustomerRefund => "Customer refund",
        CashKind.SupplierPayment => "Supplier payment",
        CashKind.Expense => "Expense",
        CashKind.PartnerCapital => "Partner capital",
        CashKind.PartnerDrawing => "Partner drawing",
        CashKind.CourierFee => "Courier charge",
        CashKind.Opening => "Opening balance",
        CashKind.Transfer => "Transfer",
        _ => "Adjustment",
    };
}
