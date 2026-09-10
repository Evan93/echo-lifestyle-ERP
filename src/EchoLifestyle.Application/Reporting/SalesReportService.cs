using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Reporting;

/// <summary>
/// What was sold, and what is still owed for it.
///
/// Read-only, and deliberately built from the orders themselves rather than
/// from a summary table kept up to date by hand. A stored total is a total that
/// can drift from the rows it claims to summarise, and the first anybody knows
/// of it is two figures on two screens that disagree. At this volume the
/// arithmetic is cheap; when it stops being cheap, that is the moment to
/// introduce a projection - not before.
/// </summary>
public class SalesReportService
{
    /// <summary>
    /// Orders that reached the courier. Anything earlier is a promise, and a
    /// promise counted as revenue is revenue that evaporates with a phone call
    /// - which in a cash-on-delivery business happens daily.
    /// </summary>
    private static readonly SalesOrderStatus[] Shipped =
        [SalesOrderStatus.Dispatched, SalesOrderStatus.Delivered];

    /// <summary>Taken, committed, not yet gone. Not revenue; still worth seeing.</summary>
    private static readonly SalesOrderStatus[] Pipeline =
        [SalesOrderStatus.Confirmed, SalesOrderStatus.Packed];

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SalesReportService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<SalesSummary> GetSummaryAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var orders = VisibleOrders().Where(o => o.OrderDate >= from && o.OrderDate <= to);

        var sold = await orders
            .Where(o => Shipped.Contains(o.Status))
            .GroupBy(o => o.OrderDate)
            .Select(g => new
            {
                Date = g.Key,
                Orders = g.Count(),
                Units = g.Sum(o => o.Lines.Sum(l => l.Quantity)),
                Sales = g.Sum(o => o.SubTotal - o.DiscountAmount),
                Delivery = g.Sum(o => o.DeliveryCharge),
                Total = g.Sum(o => o.GrandTotal),
                Cost = g.Sum(o => o.CostOfGoods),
            })
            .ToListAsync(cancellationToken);

        // Returns are counted against the day the order was taken, not the day
        // it came back. Otherwise a good week followed by a bad one looks like
        // two unrelated events rather than one, and the return rate on a
        // particular day's trading can never be worked out.
        var returned = await orders
            .Where(o => o.Status == SalesOrderStatus.Returned)
            .GroupBy(o => o.OrderDate)
            .Select(g => new
            {
                Date = g.Key,
                Orders = g.Count(),
                Value = g.Sum(o => o.GrandTotal),
            })
            .ToListAsync(cancellationToken);

        var returnsByDate = returned.ToDictionary(r => r.Date);

        // Every day in the range, including the empty ones. A gap in a sales
        // table reads as missing data; a zero reads as a quiet day, which is
        // what it was.
        var days = new List<SalesDayRow>();
        var soldByDate = sold.ToDictionary(s => s.Date);

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            soldByDate.TryGetValue(date, out var s);
            returnsByDate.TryGetValue(date, out var r);

            days.Add(new SalesDayRow
            {
                Date = date,
                Orders = s?.Orders ?? 0,
                Units = s?.Units ?? 0m,
                Sales = s?.Sales ?? 0m,
                DeliveryCharged = s?.Delivery ?? 0m,
                Total = s?.Total ?? 0m,
                Cost = s?.Cost ?? 0m,
                ReturnedOrders = r?.Orders ?? 0,
                ReturnedValue = r?.Value ?? 0m,
            });
        }

        var pipeline = await VisibleOrders()
            .Where(o => Pipeline.Contains(o.Status))
            .Select(o => o.GrandTotal)
            .ToListAsync(cancellationToken);

        return new SalesSummary
        {
            From = from,
            To = to,
            Days = days,

            // Not filtered by the date range on purpose: the pipeline is a
            // statement about right now, not about the period being reported.
            PipelineOrders = pipeline.Count,
            PipelineValue = pipeline.Sum(),
        };
    }

    /// <summary>
    /// Parcels whose money has not come back. Ordered oldest first, because the
    /// oldest is the one that needs a phone call.
    /// </summary>
    public async Task<OutstandingSummary> GetOutstandingAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var rows = await VisibleOrders()
            .Where(o => Shipped.Contains(o.Status) && o.GrandTotal > o.AmountCollected)
            .OrderBy(o => o.DispatchedAtUtc)
            .Select(o => new OutstandingOrderRow
            {
                OrderId = o.Id,
                Number = o.Number,
                OrderDate = o.OrderDate,
                CustomerName = o.RecipientName,
                DistrictName = o.DistrictName,
                CourierName = o.CourierName,
                ConsignmentNumber = o.ConsignmentNumber,
                GrandTotal = o.GrandTotal,
                AmountCollected = o.AmountCollected,
                DispatchedAtUtc = o.DispatchedAtUtc,
            })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            // Worked out here rather than in the query: EF has no portable way
            // to subtract two instants into whole days, and the alternative is
            // a raw SQL fragment that ties this to one database.
            row.DaysOut = row.DispatchedAtUtc is { } dispatched
                ? Math.Max(0, (int)(now - dispatched).TotalDays)
                : 0;
        }

        return new OutstandingSummary { Rows = rows };
    }

    /// <summary>
    /// What actually sells, over a period.
    ///
    /// Grouped by the SKU on the order line rather than by the variant it points
    /// at, so a product renamed since is still reported under the name it sold
    /// under - which is the name on the invoices this has to reconcile with.
    /// </summary>
    public async Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(
        DateOnly from,
        DateOnly to,
        int take = 25,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Composed against the same guarded set as every other report here,
        // rather than repeating the branch rule inline. Two copies of a
        // visibility rule is how one of them ends up missing a clause.
        var visible = VisibleOrders();

        return await _db.SalesOrderLines
            .AsNoTracking()
            .Where(l => visible.Any(o => o.Id == l.SalesOrderId
                                         && o.OrderDate >= from
                                         && o.OrderDate <= to
                                         && Shipped.Contains(o.Status)))
            .GroupBy(l => new { l.Sku, l.ProductName })
            .Select(g => new TopProductRow
            {
                Sku = g.Key.Sku,
                ProductName = g.Key.ProductName,
                Units = g.Sum(l => l.Quantity),
                Sales = g.Sum(l => l.LineTotal),
                Cost = g.Sum(l => l.CostOfGoods),
                Orders = g.Select(l => l.SalesOrderId).Distinct().Count(),
            })
            .OrderByDescending(r => r.Sales)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// The orders this person may report on.
    ///
    /// The same shape of guard as everywhere else: it constrains <em>staff</em>.
    /// An owner sees everything, and a branch-limited user sees their own
    /// branches - so a shop assistant cannot read the whole company's margin
    /// out of a report screen when they cannot read it off an order.
    /// </summary>
    private IQueryable<SalesOrder> VisibleOrders()
    {
        var query = _db.SalesOrders.AsNoTracking();

        if (!_currentUser.IsStaff || _currentUser.IsOwner)
        {
            return query;
        }

        var branchIds = _currentUser.BranchIds;

        return query.Where(o => branchIds.Contains(o.BranchId));
    }
}
