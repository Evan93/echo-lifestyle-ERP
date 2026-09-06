using System.Text.Json;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Auditing;
using EchoLifestyle.Infrastructure.Persistence;

namespace EchoLifestyle.Infrastructure.Auditing;

/// <summary>
/// Writes audit-trail entries. Entries are queued on the context and persisted
/// with the caller's own SaveChanges, so an action and its audit record commit
/// together or not at all.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions DetailJsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly EchoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AuditLogger(EchoDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public Task LogAsync(
        string action,
        string? entityName = null,
        string? entityId = null,
        string? summary = null,
        object? detail = null,
        long? branchId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var entry = new AuditLogEntry
        {
            OccurredAtUtc = _clock.UtcNow,
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            BranchId = branchId,
            Summary = summary,
            DetailJson = detail is null ? null : JsonSerializer.Serialize(detail, DetailJsonOptions),
            IpAddress = _currentUser.IpAddress,
            TraceId = _currentUser.TraceId,
        };

        _db.AuditLog.Add(entry);

        return Task.CompletedTask;
    }
}
