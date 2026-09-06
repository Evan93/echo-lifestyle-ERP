using EchoLifestyle.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("UserBranches", EchoDbContext.SecuritySchema);

        builder.HasKey(ub => ub.Id);

        builder.HasOne(ub => ub.Branch)
            .WithMany()
            .HasForeignKey(ub => ub.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(ub => new { ub.UserId, ub.BranchId }).IsUnique();

        builder.HasIndex(ub => ub.UserId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_UserBranches_OneDefaultPerUser");
    }
}
