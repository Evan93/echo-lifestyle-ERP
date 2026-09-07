using System.Globalization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Finance.Remittances;

/// <summary>
/// Reconciling a courier's payout.
///
/// The document is built as a draft and posted in one go, because a half-posted
/// statement is worse than none: some orders paid, some parcels back on the
/// shelf, and no way to tell which half was done. Nothing is recorded until the
/// whole thing is accepted.
///
/// Posting does three things at once, and they belong together. Collected
/// amounts become cash transactions against their orders. Returned parcels go
/// back through the same return path a single order uses, so stock lands in the
/// batches it left from. The courier's fee is recorded as money out, not netted
/// away - otherwise the cost of delivery never appears anywhere and every
/// margin figure is quietly wrong.
/// </summary>
public partial class CourierRemittanceService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly CashTransactionWriter _cash;
    private readonly SalesOrderService _orders;

    public CourierRemittanceService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        IAuditLogger audit,
        CashTransactionWriter cash,
        SalesOrderService orders)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _cash = cash;
        _orders = orders;
    }

    public async Task<OperationResult<long>> StartAsync(
        StartRemittanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CourierName))
        {
            return OperationResult<long>.Failure(
                "Which courier is this from?", nameof(StartRemittanceRequest.CourierName));
        }

        if (!await _db.Branches.AnyAsync(b => b.Id == request.BranchId && b.IsActive, cancellationToken))
        {
            return OperationResult<long>.Failure(
                "Choose an active branch.", nameof(StartRemittanceRequest.BranchId));
        }

        var today = _clock.ToBusinessDate(_clock.UtcNow);
        var date = request.RemittanceDate ?? today;

        if (date > today)
        {
            return OperationResult<long>.Failure(
                "Money cannot have arrived in the future.",
                nameof(StartRemittanceRequest.RemittanceDate));
        }

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                var remittance = new CourierRemittance
                {
                    Number = await NextNumberAsync(date, token),
                    CourierName = request.CourierName.Trim(),
                    BranchId = request.BranchId,
                    RemittanceDate = date,
                    StatementReference = Trim(request.StatementReference),
                    ReceivedVia = request.ReceivedVia,
                    Status = CourierRemittanceStatus.Draft,
                    Notes = Trim(request.Notes),
                };

                _db.CourierRemittances.Add(remittance);
                await _db.SaveChangesAsync(token);

                return OperationResult<long>.Success(remittance.Id);
            },
            cancellationToken);
    }

    /// <summary>
    /// Saves the statement as it is being typed. Safe to call repeatedly -
    /// reconciling forty consignment numbers is not done in one sitting.
    /// </summary>
    public async Task<OperationResult> SaveAsync(
        SaveRemittanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var remittance = await LoadAsync(request.Id, cancellationToken);

        if (remittance is null)
        {
            return OperationResult.Failure("That remittance no longer exists.");
        }

        if (remittance.Status != CourierRemittanceStatus.Draft)
        {
            return OperationResult.Failure(
                $"{remittance.Number} is {remittance.Status.ToString().ToLowerInvariant()} and can "
                + "no longer be edited.");
        }

        var validated = await ValidateLinesAsync(remittance, request.Lines, cancellationToken);

        if (!validated.Succeeded)
        {
            return validated;
        }

        if (request.CourierFee < 0m || request.OtherDeduction < 0m || request.NetReceived < 0m)
        {
            return OperationResult.Failure("Deductions and the amount received cannot be negative.");
        }

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                // Replaced wholesale. A draft has no cash transactions and no
                // settled orders pointing at its lines, so nothing depends on
                // their identity.
                _db.CourierRemittanceLines.RemoveRange(remittance.Lines);
                remittance.Lines.Clear();
                await _db.SaveChangesAsync(token);

                foreach (var line in request.Lines)
                {
                    remittance.Lines.Add(new CourierRemittanceLine
                    {
                        CourierRemittanceId = remittance.Id,
                        SalesOrderId = line.SalesOrderId,

                        // A returned parcel collected nothing, whatever the form
                        // posted. The database carries the same rule.
                        AmountCollected = line.IsReturned ? 0m : line.AmountCollected,
                        IsReturned = line.IsReturned,
                        ReturnReason = line.IsReturned ? Trim(line.ReturnReason) : null,
                        Notes = Trim(line.Notes),
                    });
                }

                if (request.RemittanceDate is not null)
                {
                    remittance.RemittanceDate = request.RemittanceDate.Value;
                }

                remittance.StatementReference = Trim(request.StatementReference);
                remittance.ReceivedVia = request.ReceivedVia;
                remittance.CourierFee = Round(request.CourierFee);
                remittance.OtherDeduction = Round(request.OtherDeduction);
                remittance.NetReceived = Round(request.NetReceived);
                remittance.Notes = Trim(request.Notes);

                // Gross is derived, never typed. A total somebody can type is a
                // total that stops agreeing with the lines under it.
                remittance.GrossCollected = remittance.Lines.Sum(l => l.AmountCollected);

                await _db.SaveChangesAsync(token);

                return OperationResult.Success();
            },
            cancellationToken);
    }

    /// <summary>
    /// Posts the statement: money in, fee out, returns back to stock.
    /// </summary>
    public async Task<OperationResult<RemittancePostSummary>> PostAsync(
        long id,
        bool acceptDiscrepancy = false,
        CancellationToken cancellationToken = default)
    {
        var remittance = await LoadAsync(id, cancellationToken);

        if (remittance is null)
        {
            return OperationResult<RemittancePostSummary>.Failure(
                "That remittance no longer exists.");
        }

        if (remittance.Status != CourierRemittanceStatus.Draft)
        {
            return OperationResult<RemittancePostSummary>.Failure(
                $"{remittance.Number} is already {remittance.Status.ToString().ToLowerInvariant()}.");
        }

        if (remittance.Lines.Count == 0)
        {
            return OperationResult<RemittancePostSummary>.Failure(
                $"{remittance.Number} has no orders on it.");
        }

        // The arithmetic has to work, or somebody has to say out loud that it
        // does not. Silently posting a statement that does not add up is how a
        // missing transfer goes unnoticed for a quarter.
        if (!remittance.Balances && !acceptDiscrepancy)
        {
            var gap = remittance.Discrepancy;

            return OperationResult<RemittancePostSummary>.Failure(
                $"The statement does not add up. {remittance.GrossCollected:N2} collected less "
                + $"{remittance.CourierFee:N2} in charges and {remittance.OtherDeduction:N2} other "
                + $"deductions comes to {remittance.ExpectedNet:N2}, but {remittance.NetReceived:N2} "
                + $"was received - {Math.Abs(gap):N2} "
                + (gap > 0m ? "more" : "less") + " than expected. "
                + "Check the statement, or post it anyway to record the difference.");
        }

        // Everything is validated before a single transaction is written. A
        // half-posted statement leaves some orders paid and some parcels back on
        // the shelf, with no way to tell which half ran.
        var checkedLines = await ValidateForPostingAsync(remittance, cancellationToken);

        if (!checkedLines.Succeeded)
        {
            return OperationResult<RemittancePostSummary>.Failure(checkedLines.Error!);
        }

        return await _db.ExecuteInTransactionAsync(
            async token =>
            {
                var now = _clock.UtcNow;
                var settled = 0;
                var returned = 0;

                foreach (var line in remittance.Lines)
                {
                    var order = await _db.SalesOrders
                        .Include(o => o.Lines)
                        .Include(o => o.StatusHistory)
                        .FirstAsync(o => o.Id == line.SalesOrderId, token);

                    if (line.IsReturned)
                    {
                        // The same path a single order takes, so stock lands in
                        // the batches it left from and the ledger reads the
                        // same however the return was reported.
                        var result = await _orders.RecordReturnAsync(
                            order,
                            line.ReturnReason ?? $"Returned by {remittance.CourierName}.",
                            token);

                        if (!result.Succeeded)
                        {
                            return OperationResult<RemittancePostSummary>.Failure(
                                $"{order.Number}: {result.Error}");
                        }

                        returned++;
                        continue;
                    }

                    if (line.AmountCollected > 0m)
                    {
                        await _cash.AppendAsync(
                            CashEntry.ForOrder(
                                order,
                                line.AmountCollected,
                                remittance.ReceivedVia,
                                remittance.RemittanceDate,
                                remittance.StatementReference,
                                remittance.Id,
                                line.Notes),
                            token);
                    }

                    // Delivered, whatever was collected. The parcel reached the
                    // customer; a shortfall is a debt, not a failed delivery,
                    // and the outstanding figure is what says so.
                    if (order.Status == SalesOrderStatus.Dispatched)
                    {
                        var delivered = await _orders.RecordDeliveryAsync(
                            order,

                            // The cash was already written above with the
                            // courier's method and reference. Passing zero here
                            // records the delivery without paying it twice.
                            collected: 0m,
                            note: $"Settled on {remittance.Number}.",
                            reference: remittance.StatementReference,
                            now,
                            token);

                        if (!delivered.Succeeded)
                        {
                            return OperationResult<RemittancePostSummary>.Failure(
                                $"{order.Number}: {delivered.Error}");
                        }
                    }

                    settled++;
                }

                if (remittance.CourierFee > 0m)
                {
                    // Money out, not a netting-off. Delivery is a real cost of
                    // selling, and hiding it inside the receipt would leave it
                    // absent from every margin figure in the system.
                    await _cash.AppendAsync(
                        new CashEntry
                        {
                            Direction = CashDirection.Out,
                            Kind = CashKind.CourierFee,
                            Method = remittance.ReceivedVia,
                            Amount = remittance.CourierFee,
                            TransactionDate = remittance.RemittanceDate,
                            BranchId = remittance.BranchId,
                            ReferenceNumber = remittance.StatementReference,
                            CourierRemittanceId = remittance.Id,
                            Notes = $"{remittance.CourierName} charges on {remittance.Number}.",
                        },
                        token);
                }

                if (remittance.OtherDeduction > 0m)
                {
                    await _cash.AppendAsync(
                        new CashEntry
                        {
                            Direction = CashDirection.Out,
                            Kind = CashKind.Adjustment,
                            Method = remittance.ReceivedVia,
                            Amount = remittance.OtherDeduction,
                            TransactionDate = remittance.RemittanceDate,
                            BranchId = remittance.BranchId,
                            ReferenceNumber = remittance.StatementReference,
                            CourierRemittanceId = remittance.Id,
                            Notes = $"Other deductions on {remittance.Number}.",
                        },
                        token);
                }

                remittance.Status = CourierRemittanceStatus.Posted;
                remittance.PostedAtUtc = now;
                remittance.PostedByUserId = _currentUser.UserId;

                var summary = new RemittancePostSummary
                {
                    Number = remittance.Number,
                    Settled = settled,
                    Returned = returned,
                    Collected = remittance.GrossCollected,
                    Fee = remittance.CourierFee,
                    Net = remittance.NetReceived,
                };

                await _audit.LogAsync(
                    AuditActions.CourierRemittancePosted,
                    nameof(CourierRemittance),
                    remittance.Id.ToString(CultureInfo.InvariantCulture),
                    summary.Describe()
                    + (remittance.Balances
                        ? string.Empty
                        : $" Posted with a {remittance.Discrepancy:N2} BDT discrepancy."),
                    new
                    {
                        remittance.Number,
                        remittance.CourierName,
                        remittance.StatementReference,
                        remittance.GrossCollected,
                        remittance.CourierFee,
                        remittance.OtherDeduction,
                        remittance.NetReceived,
                        remittance.Discrepancy,
                        Settled = settled,
                        Returned = returned,
                    },
                    remittance.BranchId,
                    token);

                await _db.SaveChangesAsync(token);

                return OperationResult<RemittancePostSummary>.Success(summary);
            },
            cancellationToken);
    }

    public async Task<OperationResult> CancelAsync(
        long id,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var remittance = await LoadAsync(id, cancellationToken);

        if (remittance is null)
        {
            return OperationResult.Failure("That remittance no longer exists.");
        }

        if (remittance.Status == CourierRemittanceStatus.Posted)
        {
            return OperationResult.Failure(
                $"{remittance.Number} has already posted. Its cash transactions are permanent, so a "
                + "mistake is corrected with a reversing entry rather than by cancelling this.");
        }

        remittance.Status = CourierRemittanceStatus.Cancelled;
        remittance.Notes = string.IsNullOrWhiteSpace(reason)
            ? remittance.Notes
            : $"{remittance.Notes}\nCancelled: {reason.Trim()}".TrimStart();

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    private async Task<OperationResult> ValidateLinesAsync(
        CourierRemittance remittance,
        IReadOnlyList<RemittanceLineInput> lines,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return OperationResult.Success();
        }

        var orderIds = lines.Select(l => l.SalesOrderId).ToList();

        if (orderIds.Distinct().Count() != orderIds.Count)
        {
            return OperationResult.Failure(
                "The same order appears twice on this statement. Combine those lines - settled "
                + "twice, it would be paid twice and the totals would still add up.");
        }

        var orders = await _db.SalesOrders
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.GrandTotal,
                o.AmountCollected,
                o.CourierName,
            })
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!orders.TryGetValue(line.SalesOrderId, out var order))
            {
                return OperationResult.Failure("One of the orders on this statement no longer exists.");
            }

            if (order.Status != SalesOrderStatus.Dispatched)
            {
                return OperationResult.Failure(
                    $"{order.Number} is {order.Status.ToString().ToLowerInvariant()}, not with a "
                    + "courier. Only a dispatched order can be settled from a statement.");
            }

            if (line.AmountCollected < 0m)
            {
                return OperationResult.Failure(
                    $"A negative collection on {order.Number} is a refund, which is a different "
                    + "document.");
            }

            var outstanding = order.GrandTotal - order.AmountCollected;

            if (!line.IsReturned && line.AmountCollected > outstanding)
            {
                return OperationResult.Failure(
                    $"{order.Number} only has {outstanding:N2} BDT outstanding and the statement "
                    + $"says {line.AmountCollected:N2} was collected. Check the figure - an "
                    + "overpayment is not recorded here.");
            }

            if (line.IsReturned && string.IsNullOrWhiteSpace(line.ReturnReason))
            {
                return OperationResult.Failure(
                    $"Say why {order.Number} came back. Refusal reasons are the only way to see a "
                    + "pattern before it gets expensive.");
            }
        }

        return OperationResult.Success();
    }

    /// <summary>
    /// The same checks again at posting, because a draft can sit for days while
    /// the orders on it are settled by another route.
    /// </summary>
    private Task<OperationResult> ValidateForPostingAsync(
        CourierRemittance remittance,
        CancellationToken cancellationToken) =>
        ValidateLinesAsync(
            remittance,
            remittance.Lines
                .Select(l => new RemittanceLineInput
                {
                    SalesOrderId = l.SalesOrderId,
                    AmountCollected = l.AmountCollected,
                    IsReturned = l.IsReturned,
                    ReturnReason = l.ReturnReason,
                })
                .ToList(),
            cancellationToken);

    private Task<CourierRemittance?> LoadAsync(long id, CancellationToken cancellationToken) =>
        _db.CourierRemittances
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    private async Task<string> NextNumberAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var prefix = DocumentNumber.Prefix(DocumentNumber.CourierRemittance, date);

        var used = await _db.CourierRemittances
            .Where(r => r.Number.StartsWith(prefix))
            .Select(r => r.Number)
            .ToListAsync(cancellationToken);

        return DocumentNumber.Next(prefix, used);
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
