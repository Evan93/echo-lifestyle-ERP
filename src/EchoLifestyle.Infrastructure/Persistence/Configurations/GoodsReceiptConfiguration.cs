using EchoLifestyle.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("GoodsReceipts", EchoDbContext.PurchasingSchema, table =>
        {
            // A rate of zero would silently make every imported item free.
            table.HasCheckConstraint("CK_GoodsReceipts_ExchangeRatePositive", "[ExchangeRate] > 0");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Number).HasMaxLength(40).IsRequired();
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(r => r.ExchangeRate).HasColumnType("decimal(18,6)");
        builder.Property(r => r.SupplierInvoiceNumber).HasMaxLength(60);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.SubTotal).HasColumnType("decimal(19,4)");
        builder.Property(r => r.ChargeTotal).HasColumnType("decimal(19,4)");
        builder.Property(r => r.GrandTotal).HasColumnType("decimal(19,4)");

        builder.HasOne(r => r.Supplier)
            .WithMany()
            .HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Branch)
            .WithMany()
            .HasForeignKey(r => r.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Warehouse)
            .WithMany()
            .HasForeignKey(r => r.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Numbers print on documents and are quoted back by suppliers. Two
        // receipts sharing one would make a query ambiguous.
        builder.HasIndex(r => r.Number).IsUnique();

        builder.HasIndex(r => new { r.SupplierId, r.ReceiptDate });
        builder.HasIndex(r => new { r.Status, r.ReceiptDate });
    }
}

public class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        builder.ToTable("GoodsReceiptLines", EchoDbContext.PurchasingSchema, table =>
        {
            // Receiving nothing, or negative something, is a supplier return -
            // a different document with different ledger entries.
            table.HasCheckConstraint("CK_GoodsReceiptLines_QuantityPositive", "[Quantity] > 0");
            table.HasCheckConstraint("CK_GoodsReceiptLines_UnitCostNotNegative", "[UnitCost] >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).HasColumnType("decimal(18,4)");
        builder.Property(l => l.UnitCost).HasColumnType("decimal(19,4)");
        builder.Property(l => l.DiscountAmount).HasColumnType("decimal(19,4)");
        builder.Property(l => l.LineTotal).HasColumnType("decimal(19,4)");
        builder.Property(l => l.LineTotalBase).HasColumnType("decimal(19,4)");
        builder.Property(l => l.ApportionedCharge).HasColumnType("decimal(19,4)");
        builder.Property(l => l.LandedUnitCost).HasColumnType("decimal(19,4)");
        builder.Property(l => l.BatchNumber).HasMaxLength(60);

        builder.HasOne(l => l.GoodsReceipt)
            .WithMany(r => r.Lines)
            .HasForeignKey(l => l.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.ProductVariant)
            .WithMany()
            .HasForeignKey(l => l.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.StockBatch)
            .WithMany()
            .HasForeignKey(l => l.StockBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.GoodsReceiptId);

        // "When did we last buy this, and what did it cost?" - the question the
        // purchase screen answers while somebody is typing.
        builder.HasIndex(l => l.ProductVariantId);
    }
}

public class PurchaseChargeConfiguration : IEntityTypeConfiguration<PurchaseCharge>
{
    public void Configure(EntityTypeBuilder<PurchaseCharge> builder)
    {
        builder.ToTable("PurchaseCharges", EchoDbContext.PurchasingSchema, table =>
        {
            // A negative charge is a credit note, not a cost of getting the
            // goods here, and apportioning one would reduce landed cost below
            // what was paid.
            table.HasCheckConstraint("CK_PurchaseCharges_AmountNotNegative", "[Amount] >= 0");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ChargeType).HasConversion<int>();
        builder.Property(c => c.ApportionMethod).HasConversion<int>();
        builder.Property(c => c.Description).HasMaxLength(200);
        builder.Property(c => c.Amount).HasColumnType("decimal(19,4)");

        builder.HasOne(c => c.GoodsReceipt)
            .WithMany(r => r.Charges)
            .HasForeignKey(c => c.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.GoodsReceiptId);
    }
}
