using EchoLifestyle.Domain.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class BranchWarehouseConfiguration : IEntityTypeConfiguration<BranchWarehouse>
{
    public void Configure(EntityTypeBuilder<BranchWarehouse> builder)
    {
        builder.ToTable("BranchWarehouses", EchoDbContext.AdminSchema);

        builder.HasKey(bw => bw.Id);

        builder.HasOne(bw => bw.Branch)
            .WithMany(b => b.BranchWarehouses)
            .HasForeignKey(bw => bw.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(bw => bw.Warehouse)
            .WithMany(w => w.BranchWarehouses)
            .HasForeignKey(bw => bw.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // A warehouse may only be linked to a given branch once.
        builder.HasIndex(bw => new { bw.BranchId, bw.WarehouseId }).IsUnique();

        // At most one primary warehouse per branch, enforced in the database
        // rather than trusted to application code.
        builder.HasIndex(bw => bw.BranchId)
            .IsUnique()
            .HasFilter("[IsPrimary] = 1")
            .HasDatabaseName("UX_BranchWarehouses_OnePrimaryPerBranch");
    }
}
