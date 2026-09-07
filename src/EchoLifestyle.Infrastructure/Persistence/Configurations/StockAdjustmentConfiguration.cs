using EchoLifestyle.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> builder)
    {
        builder.ToTable("StockAdjustments", EchoDbContext.InventorySchema);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Number).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Reason).HasConversion<int>();
        builder.Property(a => a.Status).HasConversion<int>();
        builder.Property(a => a.ReasonNotes).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.DecisionNotes).HasMaxLength(1000);

        builder.HasOne(a => a.Warehouse)
            .WithMany()
            .HasForeignKey(a => a.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Branch)
            .WithMany()
            .HasForeignKey(a => a.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.Number).IsUnique();

        // "What is waiting for me to approve" - the query the inventory landing
        // page runs on every load.
        builder.HasIndex(a => new { a.Status, a.AdjustmentDate });

        builder.HasIndex(a => a.AdjustmentDate);
    }
}

public class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> builder)
    {
        builder.ToTable("StockAdjustmentLines", EchoDbContext.InventorySchema, table =>
        {
            // A line that moves nothing is not a line. It would post no ledger
            // entry - the ledger refuses zero too - so storing one leaves a
            // document whose lines and whose movements disagree.
            table.HasCheckConstraint(
                "CK_StockAdjustmentLines_QuantityNotZero",
                "[QuantityChange] <> 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.QuantityChange).HasColumnType("decimal(18,4)");
        builder.Property(l => l.UnitCost).HasColumnType("decimal(19,4)");
        builder.Property(l => l.ValueChange).HasColumnType("decimal(19,4)");
        builder.Property(l => l.Notes).HasMaxLength(500);

        builder.HasOne(l => l.StockAdjustment)
            .WithMany(a => a.Lines)
            .HasForeignKey(l => l.StockAdjustmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.ProductVariant)
            .WithMany()
            .HasForeignKey(l => l.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.StockBatch)
            .WithMany()
            .HasForeignKey(l => l.StockBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        // One line per batch per document. Two lines against the same batch
        // would post two movements that have to be read together to mean
        // anything, and the reviewer sees only the second.
        builder.HasIndex(l => new { l.StockAdjustmentId, l.StockBatchId }).IsUnique();
    }
}

public class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.ToTable("StockCounts", EchoDbContext.InventorySchema);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Number).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Scope).HasConversion<int>();
        builder.Property(c => c.Status).HasConversion<int>();
        builder.Property(c => c.ScopeName).HasMaxLength(200);
        builder.Property(c => c.Notes).HasMaxLength(1000);

        builder.HasOne(c => c.Warehouse)
            .WithMany()
            .HasForeignKey(c => c.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Branch)
            .WithMany()
            .HasForeignKey(c => c.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.Number).IsUnique();

        // At most one count open per warehouse. Two people counting the same
        // shelves from two sheets produce two sets of variances against the same
        // snapshot, and posting both would double every correction.
        builder.HasIndex(c => c.WarehouseId)
            .IsUnique()
            .HasFilter("[Status] = 1")
            .HasDatabaseName("UX_StockCounts_OneOpenPerWarehouse");

        builder.HasIndex(c => new { c.Status, c.CountDate });
    }
}

public class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> builder)
    {
        builder.ToTable("StockCountLines", EchoDbContext.InventorySchema, table =>
        {
            // Counting a negative quantity is not possible on a shelf. Left
            // unconstrained, a stray minus sign becomes a write-off of twice the
            // stock.
            table.HasCheckConstraint(
                "CK_StockCountLines_CountedNotNegative",
                "[CountedQuantity] IS NULL OR [CountedQuantity] >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.SystemQuantity).HasColumnType("decimal(18,4)");
        builder.Property(l => l.CountedQuantity).HasColumnType("decimal(18,4)");
        builder.Property(l => l.LandedUnitCost).HasColumnType("decimal(19,4)");
        builder.Property(l => l.Notes).HasMaxLength(500);

        // Computed from two columns; a stored copy is one more thing that can
        // disagree with them.
        builder.Ignore(l => l.Variance);
        builder.Ignore(l => l.IsCounted);
        builder.Ignore(l => l.VarianceValue);

        builder.HasOne(l => l.StockCount)
            .WithMany(c => c.Lines)
            .HasForeignKey(l => l.StockCountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.ProductVariant)
            .WithMany()
            .HasForeignKey(l => l.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.StockBatch)
            .WithMany()
            .HasForeignKey(l => l.StockBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.StockCountId, l.StockBatchId }).IsUnique();
    }
}
