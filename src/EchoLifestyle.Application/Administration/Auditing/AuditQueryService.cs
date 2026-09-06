using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Time;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Administration.Auditing;

/// <summary>
/// Reads the audit trail. There is deliberately no write, edit or delete path
/// here - the trail is append-only, and entries are written by the services
/// that perform the actions.
/// </summary>
public class AuditQueryService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public AuditQueryService(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PagedResult<AuditListItem>> ListAsync(
        AuditFilter filter,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AuditLog.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        // Dates are entered as business dates in Dhaka and converted to a UTC
        // half-open range here. Comparing a Dhaka date against a UTC column
        // directly would silently shift entries between days by six hours.
        if (filter.FromDate is not null)
        {
            var (startUtc, _) = BusinessCalendar.BusinessDayRangeUtc(filter.FromDate.Value);
            query = query.Where(a => a.OccurredAtUtc >= startUtc);
        }

        if (filter.ToDate is not null)
        {
            var (_, endUtc) = BusinessCalendar.BusinessDayRangeUtc(filter.ToDate.Value);
            query = query.Where(a => a.OccurredAtUtc < endUtc);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(filter.Module))
        {
            var prefix = filter.Module.Trim() + ".";
            query = query.Where(a => a.Action.StartsWith(prefix));
        }

        if (!string.IsNullOrWhiteSpace(filter.UserName))
        {
            var user = filter.UserName.Trim();
            query = query.Where(a => a.UserName == user);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(a =>
                EF.Functions.Like(a.Action, $"%{term}%")
                || (a.Summary != null && EF.Functions.Like(a.Summary, $"%{term}%"))
                || (a.UserName != null && EF.Functions.Like(a.UserName, $"%{term}%"))
                || (a.EntityName != null && EF.Functions.Like(a.EntityName, $"%{term}%"))
                || (a.EntityId != null && a.EntityId == term));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        // Newest first by default: an audit trail is almost always read from
        // "what just happened" backwards.
        query = (sortColumn, sortDescending) switch
        {
            ("occurredAtLocal", false) => query.OrderBy(a => a.OccurredAtUtc),
            ("action", false) => query.OrderBy(a => a.Action).ThenByDescending(a => a.OccurredAtUtc),
            ("action", true) => query.OrderByDescending(a => a.Action).ThenByDescending(a => a.OccurredAtUtc),
            ("userName", false) => query.OrderBy(a => a.UserName).ThenByDescending(a => a.OccurredAtUtc),
            ("userName", true) => query.OrderByDescending(a => a.UserName).ThenByDescending(a => a.OccurredAtUtc),
            _ => query.OrderByDescending(a => a.OccurredAtUtc),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.OccurredAtUtc,
                a.UserName,
                a.Action,
                a.EntityName,
                a.EntityId,
                a.Summary,
                a.DetailJson,
                a.IpAddress,
                a.TraceId,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(a => new AuditListItem
            {
                Id = a.Id,
                OccurredAtUtc = a.OccurredAtUtc,
                OccurredAtLocal = _clock.ToBusinessTime(a.OccurredAtUtc).ToString("dd MMM yyyy HH:mm:ss"),
                UserName = a.UserName,
                Action = a.Action,
                Module = a.Action.Split('.')[0],
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                Summary = a.Summary,
                DetailJson = a.DetailJson,
                IpAddress = a.IpAddress,
                TraceId = a.TraceId,
            })
            .ToList();

        return new PagedResult<AuditListItem>(items, totalCount, filteredCount);
    }

    /// <summary>Distinct action keys present in the log, for the filter dropdown.</summary>
    public async Task<IReadOnlyList<string>> GetRecordedActionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.AuditLog
            .AsNoTracking()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(action => action)
            .ToListAsync(cancellationToken);
    }
}
