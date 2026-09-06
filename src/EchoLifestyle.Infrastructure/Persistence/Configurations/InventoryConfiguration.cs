using EchoLifestyle.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> builder)
    {
        builder.ToTable("StockBatches", EchoDbContext.InventorySchema);

        builder.HasKey(b => b.Id);

        builder.Property(b => b.BatchNumber).HasMaxLength(60).IsRequired();
        builder.Property(b => b.LandedUnitCost).HasColumnType("decimal(19,4)");

        builder.HasOne(b => b.ProductVariant)
            .WithMany()
            .HasForeignKey(b => b.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // One batch number per variant. The same number from two different
        // suppliers for two different products is fine; the same number twice
        // for one product would make a recall ambiguous.
        builder.HasIndex(b => new { b.ProductVariantId, b.BatchNumber }).IsUnique();

        // "What expires in the next 60 days" - the query the near-expiry screen
        // and the write-off job both run.
        builder.HasIndex(b => b.ExpiryDate);
    }
}

public class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        builder.ToTable("StockLedger", EchoDbContext.InventorySchema, table =>
        {
            // The sign has to agree with the movement type. Application code
            // already gets this right; the constraint is what makes it true for
            // a script, a fix-up query, or a future service nobody has written
            // yet. A sign error here would silently corrupt every balance
            // derived from the row.
            table.HasCheckConstraint(
                "CK_StockLedger_SignMatchesMovement",
                "([MovementType] IN (1, 3, 5, 8, 9) AND [QuantityChange] > 0) "
                + "OR ([MovementType] IN (2, 4, 6, 7, 10) AND [QuantityChange] < 0)");

            // A movement of nothing is not a movement. Allowing it would fill
            // the ledger with rows that mean nothing and hide the bug that
            // produced them.
            table.HasCheckConstraint(
                "CK_StockLedger_QuantityNotZero",
                "[QuantityChange] <> 0");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.MovementType).HasConversion<int>();
        builder.Property(e => e.DocumentType).HasConversion<int>();
        builder.Property(e => e.DocumentNumber).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(500);
        builder.Property(e => e.QuantityChange).HasColumnType("decimal(18,4)");
        builder.Property(e => e.UnitCost).HasColumnType("decimal(19,4)");
        builder.Property(e => e.ValueChange).HasColumnType("decimal(19,4)");

        builder.HasOne(e => e.ProductVariant)
            .WithMany()
            .HasForeignKey(e => e.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.StockBatch)
            .WithMany(b => b.LedgerEntries)
            .HasForeignKey(e => e.StockBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        // The rebuild query: every movement of one variant in one warehouse,
        // in order.
        builder.HasIndex(e => new { e.ProductVariantId, e.WarehouseId, e.OccurredAtUtc });

        builder.HasIndex(e => e.StockBatchId);

        // "Show me what this receipt did to stock."
        builder.HasIndex(e => new { e.DocumentType, e.DocumentId });

        builder.HasIndex(e => e.BusinessDate);
    }
}

public class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("StockBalances", EchoDbContext.InventorySchema, table =>
        {
            // Reserving more than exists would let two customers be promised
            // the same unit. Negative stock is a branch policy; negative
            // reservation is always a bug.
            table.HasCheckConstraint(
                "CK_StockBalances_ReservedNotNegative",
                "[QuantityReserved] >= 0");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.QuantityOnHand).HasColumnType("decimal(18,4)");
        builder.Property(b => b.QuantityReserved).HasColumnType("decimal(18,4)");

        // Not mapped: it is the difference between two columns, and a stored
        // copy is one more thing that can disagree with them.
        builder.Ignore(b => b.QuantityAvailable);

        builder.HasOne(b => b.ProductVariant)
            .WithMany()
            .HasForeignKey(b => b.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Warehouse)
            .WithMany()
            .HasForeignKey(b => b.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.StockBatch)
            .WithMany()
            .HasForeignKey(b => b.StockBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Exactly one row per batch per warehouse. Two would make the balance
        // depend on which one a query happened to find.
        builder.HasIndex(b => new { b.ProductVariantId, b.WarehouseId, b.StockBatchId })
            .IsUnique()
            .HasDatabaseName("UX_StockBalances_VariantWarehouseBatch");

        // "What can I sell?" - the availability read, per variant.
        builder.HasIndex(b => new { b.ProductVariantId, b.WarehouseId });
    }
}
