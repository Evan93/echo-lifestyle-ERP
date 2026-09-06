using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Common;
using EchoLifestyle.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EchoLifestyle.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps created/modified audit fields on every save, and turns deletes of
/// soft-deletable master data into updates.
///
/// This intentionally does not write audit-trail rows. Blanket row auditing
/// produces volume nobody reads; sensitive actions are logged explicitly
/// through <see cref="IAuditLogger"/> instead.
/// </summary>
public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AuditableEntityInterceptor(ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var utcNow = _clock.UtcNow;
        var userId = _currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case AuditableEntity auditable:
                    StampAuditable(entry, auditable, utcNow, userId);
                    break;

                case ApplicationUser user:
                    StampIdentityUser(entry, user, utcNow, userId);
                    break;

                case ApplicationRole role:
                    StampIdentityRole(entry, role, utcNow, userId);
                    break;
            }
        }
    }

    private static void StampAuditable(
        EntityEntry entry,
        AuditableEntity entity,
        DateTime utcNow,
        long? userId)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entity.CreatedAtUtc = utcNow;
                entity.CreatedByUserId ??= userId;
                break;

            case EntityState.Modified:
                entity.ModifiedAtUtc = utcNow;
                entity.ModifiedByUserId = userId;
                break;

            case EntityState.Deleted when entity is ISoftDeletable softDeletable:
                // Master data is retired, not removed.
                entry.State = EntityState.Modified;
                softDeletable.IsDeleted = true;
                softDeletable.DeletedAtUtc = utcNow;
                softDeletable.DeletedByUserId = userId;
                entity.ModifiedAtUtc = utcNow;
                entity.ModifiedByUserId = userId;
                break;
        }
    }

    private static void StampIdentityUser(
        EntityEntry entry,
        ApplicationUser user,
        DateTime utcNow,
        long? userId)
    {
        if (entry.State == EntityState.Added)
        {
            user.CreatedAtUtc = utcNow;
            user.CreatedByUserId ??= userId;
        }
        else if (entry.State == EntityState.Modified)
        {
            user.ModifiedAtUtc = utcNow;
            user.ModifiedByUserId = userId;
        }
    }

    private static void StampIdentityRole(
        EntityEntry entry,
        ApplicationRole role,
        DateTime utcNow,
        long? userId)
    {
        if (entry.State == EntityState.Added)
        {
            role.CreatedAtUtc = utcNow;
            role.CreatedByUserId ??= userId;
        }
        else if (entry.State == EntityState.Modified)
        {
            role.ModifiedAtUtc = utcNow;
            role.ModifiedByUserId = userId;
        }
    }
}
