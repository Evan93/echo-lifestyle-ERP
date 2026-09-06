using EchoLifestyle.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLog", EchoDbContext.AuditSchema);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(150).IsRequired();
        builder.Property(a => a.UserName).HasMaxLength(256);
        builder.Property(a => a.EntityName).HasMaxLength(150);
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.Summary).HasMaxLength(1000);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.TraceId).HasMaxLength(100);

        // The audit trail is append-only and read by date, action and actor.
        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => new { a.Action, a.OccurredAtUtc });
        builder.HasIndex(a => new { a.UserId, a.OccurredAtUtc });
        builder.HasIndex(a => new { a.EntityName, a.EntityId });
    }
}
