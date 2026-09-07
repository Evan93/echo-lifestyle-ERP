using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Finance.Remittances;

/// <summary>Reading remittances back, and finding what still needs settling.</summary>
public partial class CourierRemittanceService
{
    public async Task<PagedResult<RemittanceListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        CourierRemittanceStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CourierRemittances.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(r =>
                EF.Functions.Like(r.Number, $"%{term}%")
                || EF.Functions.Like(r.CourierName, $"%{term}%")
                || (r.StatementReference != null
                    && EF.Functions.Like(r.StatementReference, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("number", false) => query.OrderBy(r => r.Number),
            ("courier", false) => query.OrderBy(r => r.CourierName).ThenByDescending(r => r.Number),
            ("courier", true) => query.OrderByDescending(r => r.CourierName).ThenByDescending(r => r.Number),
            ("date", false) => query.OrderBy(r => r.RemittanceDate).ThenBy(r => r.Number),

            // Drafts first, then newest. An open reconciliation is why somebody
            // is on this screen.
            _ => query.OrderBy(r => r.Status == CourierRemittanceStatus.Draft ? 0 : 1)
                      .ThenByDescending(r => r.RemittanceDate)
                      .ThenByDescending(r => r.Id),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(r => new RemittanceListItem
            {
                Id = r.Id,
                Number = r.Number,
                CourierName = r.CourierName,
                RemittanceDate = r.RemittanceDate,
                StatementReference = r.StatementReference,
                Status = r.Status,
                LineCount = r.Lines.Count,
                ReturnedCount = r.Lines.Count(l => l.IsReturned),
                GrossCollected = r.GrossCollected,
                CourierFee = r.CourierFee,
                NetReceived = r.NetReceived,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<RemittanceListItem>(rows, totalCount, filteredCount);
    }

    public async Task<RemittanceDetail?> GetAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var remittance = await _db.CourierRemittances
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (remittance is null)
        {
            return null;
        }

        var lines = await _db.CourierRemittanceLines
            .AsNoTracking()
            .Where(l => l.CourierRemittanceId == id)
            .Select(l => new RemittanceLineDetail
            {
                Id = l.Id,
                SalesOrderId = l.SalesOrderId,
                OrderNumber = l.SalesOrder!.Number,
                CustomerName = l.SalesOrder.Customer!.FullName,
                DistrictName = l.SalesOrder.DistrictName,
                ConsignmentNumber = l.SalesOrder.ConsignmentNumber,
                OrderTotal = l.SalesOrder.GrandTotal,

                // What was already settled before this statement. On a draft
                // this is the live figure; on a posted one it includes this
                // statement's own collection, which is why the shortfall column
                // is only read while reconciling.
                PreviouslyCollected = l.SalesOrder.AmountCollected,
                AmountCollected = l.AmountCollected,
                IsReturned = l.IsReturned,
                ReturnReason = l.ReturnReason,
                Notes = l.Notes,
            })
            .ToListAsync(cancellationToken);

        return new RemittanceDetail
        {
            Id = remittance.Id,
            Number = remittance.Number,
            CourierName = remittance.CourierName,
            BranchId = remittance.BranchId,
            RemittanceDate = remittance.RemittanceDate,
            StatementReference = remittance.StatementReference,
            Status = remittance.Status,
            ReceivedVia = remittance.ReceivedVia,
            GrossCollected = remittance.GrossCollected,
            CourierFee = remittance.CourierFee,
            OtherDeduction = remittance.OtherDeduction,
            NetReceived = remittance.NetReceived,
            Notes = remittance.Notes,
            PostedAtUtc = remittance.PostedAtUtc,
            Lines = lines.OrderBy(l => l.OrderNumber).ToList(),
        };
    }

    /// <summary>
    /// Orders this courier is still holding money for.
    ///
    /// Dispatched, not yet settled, and sorted by how long they have been out -
    /// the ones at the top are the ones worth a phone call. This is the list
    /// somebody ticks off against the statement.
    /// </summary>
    public async Task<IReadOnlyList<UnsettledOrder>> GetUnsettledAsync(
        string courierName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courierName))
        {
            return [];
        }

        var name = courierName.Trim();
        var today = _clock.ToBusinessDate(_clock.UtcNow);

        var rows = await _db.SalesOrders
            .AsNoTracking()
            .Where(o => o.Status == SalesOrderStatus.Dispatched
                        && o.CourierName == name

                        // Anything already on an open statement is somebody
                        // else's problem right now - listing it twice invites
                        // settling it twice.
                        && !_db.CourierRemittanceLines.Any(
                            l => l.SalesOrderId == o.Id
                                 && l.CourierRemittance!.Status != CourierRemittanceStatus.Cancelled))
            .Select(o => new UnsettledOrder
            {
                SalesOrderId = o.Id,
                Number = o.Number,
                CustomerName = o.Customer!.FullName,
                DistrictName = o.DistrictName,
                ConsignmentNumber = o.ConsignmentNumber,
                OrderDate = o.OrderDate,
                DispatchedAtUtc = o.DispatchedAtUtc,
                GrandTotal = o.GrandTotal,
                AlreadyCollected = o.AmountCollected,
            })
            .ToListAsync(cancellationToken);

        // Day arithmetic in memory: DateOnly subtraction does not translate, and
        // this list is a statement's worth of rows by definition.
        foreach (var row in rows)
        {
            row.DaysWithCourier = row.DispatchedAtUtc is null
                ? 0
                : today.DayNumber - DateOnly.FromDateTime(row.DispatchedAtUtc.Value).DayNumber;
        }

        return rows
            .OrderByDescending(r => r.DaysWithCourier)
            .ThenBy(r => r.Number)
            .ToList();
    }

    /// <summary>
    /// Every courier currently holding stock, with what they owe.
    ///
    /// The screen an owner opens on a Sunday morning: who has our money, how
    /// much, and for how long.
    /// </summary>
    public async Task<IReadOnlyList<CourierExposure>> GetExposureAsync(
        CancellationToken cancellationToken = default)
    {
        var today = _clock.ToBusinessDate(_clock.UtcNow);

        var rows = await _db.SalesOrders
            .AsNoTracking()
            .Where(o => o.Status == SalesOrderStatus.Dispatched && o.CourierName != null)
            .Select(o => new
            {
                Courier = o.CourierName!,
                Outstanding = o.GrandTotal - o.AmountCollected,
                o.DispatchedAtUtc,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Courier)
            .Select(g => new CourierExposure
            {
                CourierName = g.Key,
                Parcels = g.Count(),
                Outstanding = g.Sum(r => r.Outstanding),
                OldestDays = g.Max(r => r.DispatchedAtUtc is null
                    ? 0
                    : today.DayNumber - DateOnly.FromDateTime(r.DispatchedAtUtc.Value).DayNumber),
            })
            .OrderByDescending(c => c.Outstanding)
            .ToList();
    }
}

/// <summary>What one courier is holding.</summary>
public class CourierExposure
{
    public string CourierName { get; set; } = string.Empty;

    public int Parcels { get; set; }

    public decimal Outstanding { get; set; }

    /// <summary>Days since the oldest unsettled parcel went out.</summary>
    public int OldestDays { get; set; }
}
