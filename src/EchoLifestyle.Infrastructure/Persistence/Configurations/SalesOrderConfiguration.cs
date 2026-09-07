using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("SalesOrders", EchoDbContext.SalesSchema, table =>
        {
            // More collected than was ever owed is not generosity, it is a
            // typo or a courier reconciliation error, and it would quietly
            // overstate revenue.
            table.HasCheckConstraint(
                "CK_SalesOrders_CollectedNotAboveTotal",
                "[AmountCollected] <= [GrandTotal]");

            table.HasCheckConstraint(
                "CK_SalesOrders_AmountsNotNegative",
                "[SubTotal] >= 0 AND [DiscountAmount] >= 0 AND [DeliveryCharge] >= 0 "
                + "AND [AmountCollected] >= 0");
        });

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Number).HasMaxLength(40).IsRequired();
        builder.Property(o => o.Status).HasConversion<int>();
        builder.Property(o => o.Channel).HasConversion<int>();
        builder.Property(o => o.PaymentMethod).HasConversion<int>();

        builder.Property(o => o.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.RecipientPhone).HasMaxLength(11).IsFixedLength().IsRequired();
        builder.Property(o => o.DivisionName).HasMaxLength(60).IsRequired();
        builder.Property(o => o.DistrictName).HasMaxLength(60).IsRequired();
        builder.Property(o => o.AreaOrThana).HasMaxLength(150).IsRequired();
        builder.Property(o => o.AddressLine).HasMaxLength(500).IsRequired();
        builder.Property(o => o.Landmark).HasMaxLength(200);
        builder.Property(o => o.PostCode).HasMaxLength(10);
        builder.Property(o => o.DeliveryNotes).HasMaxLength(500);

        builder.Property(o => o.SubTotal).HasColumnType("decimal(19,4)");
        builder.Property(o => o.DiscountAmount).HasColumnType("decimal(19,4)");
        builder.Property(o => o.DeliveryCharge).HasColumnType("decimal(19,4)");
        builder.Property(o => o.GrandTotal).HasColumnType("decimal(19,4)");
        builder.Property(o => o.AmountCollected).HasColumnType("decimal(19,4)");
        builder.Property(o => o.CostOfGoods).HasColumnType("decimal(19,4)");

        builder.Property(o => o.CourierName).HasMaxLength(100);
        builder.Property(o => o.ConsignmentNumber).HasMaxLength(60);
        builder.Property(o => o.CancelReason).HasMaxLength(500);
        builder.Property(o => o.ReturnReason).HasMaxLength(500);
        builder.Property(o => o.Notes).HasMaxLength(2000);

        // Computed from stored columns; a stored copy is one more thing that
        // can disagree with them.
        builder.Ignore(o => o.AmountOutstanding);

        builder.HasOne(o => o.Customer)
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Branch)
            .WithMany()
            .HasForeignKey(o => o.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Warehouse)
            .WithMany()
            .HasForeignKey(o => o.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => o.Number).IsUnique();

        // "What is waiting to be packed?" - the query the whole day runs on.
        builder.HasIndex(o => new { o.Status, o.OrderDate });

        builder.HasIndex(o => new { o.CustomerId, o.OrderDate });
        builder.HasIndex(o => o.OrderDate);

        // Chasing a parcel by the number the courier gave. Filtered, because
        // most orders have none until they ship.
        builder.HasIndex(o => o.ConsignmentNumber)
            .HasFilter("[ConsignmentNumber] IS NOT NULL");
    }
}

public class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("SalesOrderLines", EchoDbContext.SalesSchema, table =>
        {
            table.HasCheckConstraint("CK_SalesOrderLines_QuantityPositive", "[Quantity] > 0");

            // A negative price is a refund, which is a different document with
            // different ledger entries.
            table.HasCheckConstraint(
                "CK_SalesOrderLines_PriceNotNegative",
                "[UnitPrice] >= 0 AND [DiscountAmount] >= 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Sku).HasMaxLength(60).IsRequired();
        builder.Property(l => l.ProductName).HasMaxLength(250).IsRequired();
        builder.Property(l => l.VariantName).HasMaxLength(150).IsRequired();
        builder.Property(l => l.Quantity).HasColumnType("decimal(18,4)");
        builder.Property(l => l.UnitPrice).HasColumnType("decimal(19,4)");
        builder.Property(l => l.DiscountAmount).HasColumnType("decimal(19,4)");
        builder.Property(l => l.LineTotal).HasColumnType("decimal(19,4)");
        builder.Property(l => l.CostOfGoods).HasColumnType("decimal(19,4)");
        builder.Property(l => l.Notes).HasMaxLength(500);

        builder.Ignore(l => l.Margin);

        builder.HasOne(l => l.SalesOrder)
            .WithMany(o => o.Lines)
            .HasForeignKey(l => l.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.ProductVariant)
            .WithMany()
            .HasForeignKey(l => l.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.SalesOrderId);

        // "What has this product sold?" - every sales report starts here.
        builder.HasIndex(l => l.ProductVariantId);
    }
}

public class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations", EchoDbContext.InventorySchema, table =>
        {
            table.HasCheckConstraint("CK_StockReservations_QuantityPositive", "[Quantity] > 0");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Quantity).HasColumnType("decimal(18,4)");

        builder.HasOne(r => r.SalesOrder)
            .WithMany(o => o.Reservations)
            .HasForeignKey(r => r.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.SalesOrderLine)
            .WithMany()
            .HasForeignKey(r => r.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.StockBatch)
            .WithMany()
            .HasForeignKey(r => r.StockBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.SalesOrderId);

        // "What is holding this batch?" - the question asked when the reserved
        // column looks wrong and somebody needs to find out why.
        builder.HasIndex(r => new { r.StockBatchId, r.WarehouseId });
    }
}

public class SalesOrderStatusChangeConfiguration : IEntityTypeConfiguration<SalesOrderStatusChange>
{
    public void Configure(EntityTypeBuilder<SalesOrderStatusChange> builder)
    {
        builder.ToTable("SalesOrderStatusChanges", EchoDbContext.SalesSchema);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.FromStatus).HasConversion<int>();
        builder.Property(c => c.ToStatus).HasConversion<int>();
        builder.Property(c => c.Note).HasMaxLength(500);

        builder.HasOne(c => c.SalesOrder)
            .WithMany(o => o.StatusHistory)
            .HasForeignKey(c => c.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.SalesOrderId, c.OccurredAtUtc });
    }
}
