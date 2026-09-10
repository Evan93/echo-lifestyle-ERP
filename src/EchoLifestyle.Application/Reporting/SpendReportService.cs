using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Finance.Cash;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Reporting;

/// <summary>
/// Where the money goes.
///
/// Answers the question an owner actually asks - "what did we spend last
/// month?" - which the existing screens could only answer in pieces: courier
/// fees on the remittance screen, advertising on the expenses screen, stock
/// buying nowhere at all.
/// </summary>
public class SpendReportService
{
    /// <summary>
    /// The kinds that are money leaving. <see cref="CashKind.Transfer"/> is
    /// deliberately absent: moving cash from the till to bKash is not spending,
    /// and counting it would inflate the total by an amount that never left the
    /// business.
    /// </summary>
    private static readonly CashKind[] OutgoingKinds =
    [
        CashKind.SupplierPayment,
        CashKind.CourierFee,
        CashKind.Expense,
        CashKind.CustomerRefund,
        CashKind.PartnerDrawing,
    ];

    private readonly IApplicationDbContext _db;
    private readonly CashService _cash;

    public SpendReportService(IApplicationDbContext db, CashService cash)
    {
        _db = db;
        _cash = cash;
    }

    public async Task<SpendReport> GetAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Grouped on stored columns, then named afterwards. Same reasoning as
        // the expense breakdown next door: grouping on a navigation property
        // reads better and is one translation quirk away from pulling the whole
        // cash log into memory.
        var raw = await _db.CashTransactions
            .AsNoTracking()
            .Where(t => OutgoingKinds.Contains(t.Kind)
                        && t.TransactionDate >= from
                        && t.TransactionDate <= to)
            .GroupBy(t => new { t.Kind, t.Direction })
            .Select(g => new
            {
                g.Key.Kind,
                g.Key.Direction,
                Total = g.Sum(t => t.Amount),
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        var cashOut = raw
            .GroupBy(r => r.Kind)
            .Select(g => new SpendKindRow
            {
                Name = Describe(g.Key),

                // Out is the spending; In on one of these kinds can only be a
                // reversal of one, so it subtracts.
                Amount = g.Sum(r => r.Direction == CashDirection.Out ? r.Total : -r.Total),
                Entries = g.Sum(r => r.Count),
                IsCostOfSale = g.Key == CashKind.CourierFee,
            })
            .Where(r => r.Amount != 0m)
            .OrderByDescending(r => r.Amount)
            .ToList();

        // Asked of the finance module rather than queried again. The expense
        // breakdown already knows which categories are costs of sale, and a
        // second implementation here would be a second opinion about the same
        // rows.
        var expenses = await _cash.GetExpenseBreakdownAsync(from, to, cancellationToken);

        // Cost-of-sale on the Expense line is whatever the category flags say,
        // so the split has to come from the breakdown rather than from the kind.
        var expenseRow = cashOut.FirstOrDefault(r => r.Name == Describe(CashKind.Expense));

        if (expenseRow is not null && expenses.Total != 0m)
        {
            // Split the single Expense line in two, so "cost of sale" on this
            // report means the same thing it means on the expenses screen.
            cashOut.Remove(expenseRow);

            if (expenses.CostOfSale != 0m)
            {
                cashOut.Add(new SpendKindRow
                {
                    Name = "Expenses that rise with volume",
                    Amount = expenses.CostOfSale,
                    Entries = expenses.Rows.Where(r => r.IsCostOfSale).Sum(r => r.Entries),
                    IsCostOfSale = true,
                });
            }

            if (expenses.Operating != 0m)
            {
                cashOut.Add(new SpendKindRow
                {
                    Name = "Running costs",
                    Amount = expenses.Operating,
                    Entries = expenses.Rows.Where(r => !r.IsCostOfSale).Sum(r => r.Entries),
                    IsCostOfSale = false,
                });
            }

            cashOut = cashOut.OrderByDescending(r => r.Amount).ToList();
        }

        var total = cashOut.Sum(r => r.Amount);

        foreach (var row in cashOut)
        {
            row.ShareOfTotal = total == 0m ? 0m : Math.Round(row.Amount / total * 100m, 1);
        }

        // Posted receipts only. A draft is stock somebody is still typing, and
        // a cancelled one is stock that never arrived - neither was bought.
        var purchases = await _db.GoodsReceipts
            .AsNoTracking()
            .Where(r => r.Status == GoodsReceiptStatus.Posted
                        && r.ReceiptDate >= from
                        && r.ReceiptDate <= to)
            .GroupBy(r => new { r.SupplierId, r.Supplier!.Name })
            .Select(g => new PurchaseSupplierRow
            {
                SupplierId = g.Key.SupplierId,
                SupplierName = g.Key.Name,
                Receipts = g.Count(),
                Goods = g.Sum(r => r.SubTotal),
                Charges = g.Sum(r => r.ChargeTotal),
            })
            .ToListAsync(cancellationToken);

        return new SpendReport
        {
            From = from,
            To = to,
            CashOut = cashOut,
            Expenses = expenses.Rows,
            Purchases = purchases.OrderByDescending(p => p.Goods + p.Charges).ToList(),
        };
    }

    /// <summary>
    /// The kind in the words the owner uses, not the words the enum uses.
    /// "CourierFee" is what Steadfast keeps out of a payout; nobody calls it
    /// that out loud.
    /// </summary>
    private static string Describe(CashKind kind) => kind switch
    {
        CashKind.SupplierPayment => "Paid to suppliers",
        CashKind.CourierFee => "Courier charges",
        CashKind.Expense => "Expenses",
        CashKind.CustomerRefund => "Refunded to customers",
        CashKind.PartnerDrawing => "Taken out by partners",
        _ => kind.ToString(),
    };
}
