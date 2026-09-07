using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Finance.Cash;

/// <summary>Reading the money log back, and the three summaries worth having.</summary>
public partial class CashService
{
    public async Task<PagedResult<CashListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        CashKind? kind,
        CashDirection? direction,
        PaymentMethodKind? method,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CashTransactions.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        if (kind is not null)
        {
            query = query.Where(t => t.Kind == kind);
        }

        if (direction is not null)
        {
            query = query.Where(t => t.Direction == direction);
        }

        if (method is not null)
        {
            query = query.Where(t => t.Method == method);
        }

        if (from is not null)
        {
            query = query.Where(t => t.TransactionDate >= from);
        }

        if (to is not null)
        {
            query = query.Where(t => t.TransactionDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(t =>
                EF.Functions.Like(t.Number, $"%{term}%")
                || (t.ReferenceNumber != null && EF.Functions.Like(t.ReferenceNumber, $"%{term}%"))
                || (t.Notes != null && EF.Functions.Like(t.Notes, $"%{term}%"))
                || (t.SalesOrder != null && EF.Functions.Like(t.SalesOrder.Number, $"%{term}%"))
                || (t.ExpenseCategory != null
                    && EF.Functions.Like(t.ExpenseCategory.Name, $"%{term}%"))
                || (t.Supplier != null && EF.Functions.Like(t.Supplier.Name, $"%{term}%"))
                || (t.Partner != null && EF.Functions.Like(t.Partner.Name, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("number", false) => query.OrderBy(t => t.Number),
            ("number", true) => query.OrderByDescending(t => t.Number),
            ("amount", false) => query.OrderBy(t => t.Amount),
            ("amount", true) => query.OrderByDescending(t => t.Amount),
            ("date", false) => query.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id),

            // Newest first. A money log is read from the end.
            _ => query.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(t => new CashListItem
            {
                Id = t.Id,
                Number = t.Number,
                TransactionDate = t.TransactionDate,
                Kind = t.Kind,
                Direction = t.Direction,
                Method = t.Method,
                Amount = t.Amount,

                // Whoever the entry is about. One column, because on a money log
                // the party is never ambiguous - an expense has a category, a
                // payment has a supplier, and no row has both.
                Party = t.ExpenseCategory != null ? t.ExpenseCategory.Name
                    : t.Partner != null ? t.Partner.Name
                    : t.Supplier != null ? t.Supplier.Name
                    : t.Customer != null ? t.Customer.FullName
                    : null,

                OrderNumber = t.SalesOrder != null ? t.SalesOrder.Number : null,
                SalesOrderId = t.SalesOrderId,
                ReferenceNumber = t.ReferenceNumber,
                Notes = t.Notes,
                IsReversal = t.ReversesCashTransactionId != null,
                IsReversed = _db.CashTransactions.Any(r => r.ReversesCashTransactionId == t.Id),
                CourierRemittanceId = t.CourierRemittanceId,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<CashListItem>(rows, totalCount, filteredCount);
    }

    public async Task<CashDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var detail = await _db.CashTransactions
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new CashDetail
            {
                Id = t.Id,
                Number = t.Number,
                TransactionDate = t.TransactionDate,
                Kind = t.Kind,
                Direction = t.Direction,
                Method = t.Method,
                Amount = t.Amount,
                ReferenceNumber = t.ReferenceNumber,
                Notes = t.Notes,
                BranchName = t.Branch != null ? t.Branch.Name : string.Empty,
                ExpenseCategoryName = t.ExpenseCategory != null ? t.ExpenseCategory.Name : null,
                PartnerName = t.Partner != null ? t.Partner.Name : null,
                SupplierName = t.Supplier != null ? t.Supplier.Name : null,
                CustomerName = t.Customer != null ? t.Customer.FullName : null,
                OrderNumber = t.SalesOrder != null ? t.SalesOrder.Number : null,
                SalesOrderId = t.SalesOrderId,
                CourierRemittanceId = t.CourierRemittanceId,
                CreatedAtUtc = t.CreatedAtUtc,
                IsReversal = t.ReversesCashTransactionId != null,
                ReversesId = t.ReversesCashTransactionId,
                ReversesNumber = t.Reverses != null ? t.Reverses.Number : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (detail is null)
        {
            return null;
        }

        var reversal = await _db.CashTransactions
            .AsNoTracking()
            .Where(t => t.ReversesCashTransactionId == id)
            .Select(t => new { t.Id, t.Number })
            .FirstOrDefaultAsync(cancellationToken);

        if (reversal is not null)
        {
            detail.IsReversed = true;
            detail.ReversedById = reversal.Id;
            detail.ReversedByNumber = reversal.Number;
        }

        return detail;
    }

    /// <summary>
    /// What is in each pot: opening, in, out, closing, by payment method.
    ///
    /// The screen that gets checked against a physical cash box and a bKash
    /// balance, which makes it the only thing that ever finds a wrongly
    /// recorded entry. Opening is everything before the period, netted - not a
    /// stored figure, because a stored opening balance is one more number that
    /// can disagree with the log behind it.
    /// </summary>
    public async Task<CashPosition> GetPositionAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var opening = await _db.CashTransactions
            .AsNoTracking()
            .Where(t => t.TransactionDate < from)
            .GroupBy(t => new { t.Method, t.Direction })
            .Select(g => new { g.Key.Method, g.Key.Direction, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        var period = await _db.CashTransactions
            .AsNoTracking()
            .Where(t => t.TransactionDate >= from && t.TransactionDate <= to)
            .GroupBy(t => new { t.Method, t.Direction })
            .Select(g => new
            {
                g.Key.Method,
                g.Key.Direction,
                Total = g.Sum(t => t.Amount),
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        var methods = opening.Select(o => o.Method)
            .Concat(period.Select(p => p.Method))
            .Distinct()
            .OrderBy(m => (int)m)
            .ToList();

        var rows = methods
            .Select(method => new CashPositionRow
            {
                Method = method,
                Opening = opening.Where(o => o.Method == method)
                    .Sum(o => o.Direction == CashDirection.In ? o.Total : -o.Total),
                In = period.Where(p => p.Method == method && p.Direction == CashDirection.In)
                    .Sum(p => p.Total),
                Out = period.Where(p => p.Method == method && p.Direction == CashDirection.Out)
                    .Sum(p => p.Total),
                Entries = period.Where(p => p.Method == method).Sum(p => p.Count),
            })
            .ToList();

        return new CashPosition { From = from, To = to, Rows = rows };
    }

    /// <summary>
    /// Where the money went, by category, over a period.
    ///
    /// Summed signed, so a reversal cancels the entry it reverses and the
    /// category total is what was actually spent rather than what was ever
    /// typed.
    /// </summary>
    public async Task<ExpenseBreakdown> GetExpenseBreakdownAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Grouped on stored columns only, then named from the category table.
        // Grouping on a navigation's name instead reads better and is one
        // translation quirk away from being evaluated in memory over the whole
        // log.
        var raw = await _db.CashTransactions
            .AsNoTracking()
            .Where(t => t.Kind == CashKind.Expense
                        && t.TransactionDate >= from
                        && t.TransactionDate <= to)
            .GroupBy(t => new { t.ExpenseCategoryId, t.Direction })
            .Select(g => new
            {
                g.Key.ExpenseCategoryId,
                g.Key.Direction,
                Total = g.Sum(t => t.Amount),
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        if (raw.Count == 0)
        {
            return new ExpenseBreakdown { From = from, To = to, Rows = [] };
        }

        var categoryIds = raw
            .Where(r => r.ExpenseCategoryId is not null)
            .Select(r => r.ExpenseCategoryId!.Value)
            .Distinct()
            .ToList();

        var categories = await _db.ExpenseCategories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.IsCostOfSale })
            .ToListAsync(cancellationToken);

        var rows = raw
            .GroupBy(r => r.ExpenseCategoryId)
            .Select(g =>
            {
                var category = categories.FirstOrDefault(c => c.Id == g.Key);

                return new ExpenseBreakdownRow
                {
                    ExpenseCategoryId = g.Key,
                    CategoryName = category?.Name ?? "Uncategorised",
                    IsCostOfSale = category?.IsCostOfSale ?? false,

                    // Out is the spending; In can only be a reversal of one.
                    Amount = g.Sum(r => r.Direction == CashDirection.Out ? r.Total : -r.Total),
                    Entries = g.Sum(r => r.Count),
                };
            })
            .OrderByDescending(r => r.Amount)
            .ToList();

        var total = rows.Sum(r => r.Amount);

        foreach (var row in rows)
        {
            row.ShareOfTotal = total == 0m ? 0m : decimal.Round(row.Amount / total * 100m, 1);
        }

        return new ExpenseBreakdown { From = from, To = to, Rows = rows };
    }

    /// <summary>
    /// What each partner has put in and taken out, over all time.
    ///
    /// Deliberately not filtered by period: a capital account is cumulative, and
    /// a partner asking "what am I in for?" means since the beginning.
    /// </summary>
    public async Task<IReadOnlyList<PartnerLedgerRow>> GetPartnerLedgerAsync(
        CancellationToken cancellationToken = default)
    {
        var partners = await _db.Partners
            .AsNoTracking()
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .Select(p => new PartnerLedgerRow
            {
                PartnerId = p.Id,
                Name = p.Name,
                OwnershipPercent = p.OwnershipPercent,
                IsActive = p.IsActive,
            })
            .ToListAsync(cancellationToken);

        if (partners.Count == 0)
        {
            return partners;
        }

        var movements = await _db.CashTransactions
            .AsNoTracking()
            .Where(t => t.PartnerId != null
                        && (t.Kind == CashKind.PartnerCapital || t.Kind == CashKind.PartnerDrawing))
            .GroupBy(t => new { t.PartnerId, t.Kind, t.Direction })
            .Select(g => new
            {
                g.Key.PartnerId,
                g.Key.Kind,
                g.Key.Direction,
                Total = g.Sum(t => t.Amount),
                Last = g.Max(t => t.TransactionDate),
            })
            .ToListAsync(cancellationToken);

        foreach (var partner in partners)
        {
            var mine = movements.Where(m => m.PartnerId == partner.PartnerId).ToList();

            if (mine.Count == 0)
            {
                continue;
            }

            // Signed within each kind, so a reversed contribution reduces
            // capital rather than appearing as a drawing.
            partner.CapitalIn = mine
                .Where(m => m.Kind == CashKind.PartnerCapital)
                .Sum(m => m.Direction == CashDirection.In ? m.Total : -m.Total);

            partner.Drawings = mine
                .Where(m => m.Kind == CashKind.PartnerDrawing)
                .Sum(m => m.Direction == CashDirection.Out ? m.Total : -m.Total);

            partner.LastMovement = mine.Max(m => m.Last);
        }

        return partners;
    }

    public async Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(
        CancellationToken cancellationToken = default) =>
        await _db.ExpenseCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new ExpenseCategoryOption
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                IsCostOfSale = c.IsCostOfSale,
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PartnerOption>> GetPartnersAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Partners
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .Select(p => new PartnerOption { Id = p.Id, Name = p.Name })
            .ToListAsync(cancellationToken);
}
