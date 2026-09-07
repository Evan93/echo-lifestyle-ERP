using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Finance;

/// <summary>
/// The one place money is recorded.
///
/// The same arrangement as <c>StockMovementWriter</c>, deliberately: it appends
/// the transaction and moves the figure that mirrors it, in one call, so the two
/// cannot be separated by a later edit. An order's collected amount is a
/// projection of the transactions against it, exactly as a stock balance is a
/// projection of the ledger.
///
/// That is what makes "why does this order say it is paid?" answerable. A
/// collected column somebody can type into answers nothing.
///
/// Nothing here saves or opens a transaction. The caller owns both - a
/// remittage settles every order on the statement or none of them.
/// </summary>
public class CashTransactionWriter
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CashTransactionWriter(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Records money moving, and updates the order it settles.
    /// </summary>
    /// <returns>The transaction, so a caller can reference it.</returns>
    public async Task<CashTransaction> AppendAsync(
        CashEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Amount <= 0m)
        {
            // The database says the same thing. Failing here gives a stack trace
            // pointing at the bug rather than a constraint error pointing at the
            // symptom.
            throw new ArgumentException(
                "A cash transaction of zero or less is not a transaction. Direction says which way "
                + "it went.",
                nameof(entry));
        }

        var transaction = new CashTransaction
        {
            Number = await NextNumberAsync(entry.TransactionDate, cancellationToken),
            Direction = entry.Direction,
            Kind = entry.Kind,
            Method = entry.Method,
            Amount = decimal.Round(entry.Amount, 4, MidpointRounding.AwayFromZero),
            TransactionDate = entry.TransactionDate,
            ReferenceNumber = Trim(entry.ReferenceNumber),
            SalesOrderId = entry.SalesOrderId,
            CustomerId = entry.CustomerId,
            SupplierId = entry.SupplierId,
            ExpenseCategoryId = entry.ExpenseCategoryId,
            PartnerId = entry.PartnerId,
            CourierRemittanceId = entry.CourierRemittanceId,
            ReversesCashTransactionId = entry.ReversesCashTransactionId,
            BranchId = entry.BranchId,
            Notes = Trim(entry.Notes),
        };

        _db.CashTransactions.Add(transaction);

        // The order's collected figure moves with the transaction that justifies
        // it, in the same call. Refunds subtract, which is why direction is read
        // rather than assumed.
        if (entry.SalesOrderId is not null && entry.Kind
                is CashKind.OrderCollection or CashKind.CustomerRefund)
        {
            var order = await _db.SalesOrders
                .FirstOrDefaultAsync(o => o.Id == entry.SalesOrderId, cancellationToken);

            if (order is not null)
            {
                order.AmountCollected += entry.Direction == CashDirection.In
                    ? transaction.Amount
                    : -transaction.Amount;

                order.CollectedAtUtc = order.AmountCollected > 0m ? _clock.UtcNow : null;
            }
        }

        return transaction;
    }

    /// <summary>
    /// What an order has actually been paid, summed from the transactions.
    ///
    /// The truth behind the projection. Used by the reconciliation screen and by
    /// anything that needs to be certain rather than fast.
    /// </summary>
    public async Task<decimal> CollectedForOrderAsync(
        long salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.CashTransactions
            .AsNoTracking()
            .Where(t => t.SalesOrderId == salesOrderId
                        && (t.Kind == CashKind.OrderCollection || t.Kind == CashKind.CustomerRefund))
            .Select(t => new { t.Direction, t.Amount })
            .ToListAsync(cancellationToken);

        return rows.Sum(t => t.Direction == CashDirection.In ? t.Amount : -t.Amount);
    }

    /// <summary>
    /// CT-YYMM-00001. Read inside the posting transaction; see
    /// <see cref="DocumentNumber"/> for why that matters.
    /// </summary>
    private async Task<string> NextNumberAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var prefix = DocumentNumber.Prefix(DocumentNumber.CashTransaction, date);

        var used = await _db.CashTransactions
            .Where(t => t.Number.StartsWith(prefix))
            .Select(t => t.Number)
            .ToListAsync(cancellationToken);

        return DocumentNumber.Next(prefix, used, width: 5);
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One movement of money, described in full.</summary>
public class CashEntry
{
    public required CashDirection Direction { get; init; }

    public required CashKind Kind { get; init; }

    public required PaymentMethodKind Method { get; init; }

    /// <summary>Always positive.</summary>
    public required decimal Amount { get; init; }

    public required DateOnly TransactionDate { get; init; }

    public required long BranchId { get; init; }

    public string? ReferenceNumber { get; init; }

    public long? SalesOrderId { get; init; }

    public long? CustomerId { get; init; }

    public long? SupplierId { get; init; }

    public long? ExpenseCategoryId { get; init; }

    public long? PartnerId { get; init; }

    public long? CourierRemittanceId { get; init; }

    /// <summary>Set only by <c>CashService.ReverseAsync</c>.</summary>
    public long? ReversesCashTransactionId { get; init; }

    public string? Notes { get; init; }

    /// <summary>A customer paying for an order, by whatever means.</summary>
    public static CashEntry ForOrder(
        SalesOrder order,
        decimal amount,
        PaymentMethodKind method,
        DateOnly date,
        string? reference = null,
        long? remittanceId = null,
        string? notes = null) => new()
        {
            Direction = CashDirection.In,
            Kind = CashKind.OrderCollection,
            Method = method,
            Amount = amount,
            TransactionDate = date,
            BranchId = order.BranchId,
            ReferenceNumber = reference,
            SalesOrderId = order.Id,
            CustomerId = order.CustomerId,
            CourierRemittanceId = remittanceId,
            Notes = notes,
        };
}
