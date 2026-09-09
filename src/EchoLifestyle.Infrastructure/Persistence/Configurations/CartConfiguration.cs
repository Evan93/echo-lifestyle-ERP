using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts", EchoDbContext.SalesSchema);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Token).HasMaxLength(64).IsRequired();

        // The token is the credential. Unique so two baskets can never collide,
        // and indexed because every page load on the storefront looks a basket
        // up by it.
        builder.HasIndex(c => c.Token).IsUnique();

        builder.Ignore(c => c.IsConverted);

        builder.HasOne(c => c.Customer)
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ConvertedToSalesOrder)
            .WithMany()
            .HasForeignKey(c => c.ConvertedToSalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // One basket per order. A second cart claiming the same order would
        // mean the same goods were checked out twice.
        builder.HasIndex(c => c.ConvertedToSalesOrderId)
            .IsUnique()
            .HasFilter("[ConvertedToSalesOrderId] IS NOT NULL");

        // What a cleanup job sweeps by, once there is one.
        builder.HasIndex(c => c.LastTouchedAtUtc);
    }
}

public class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        builder.ToTable("CartLines", EchoDbContext.SalesSchema, table =>
        {
            table.HasCheckConstraint("CK_CartLines_QuantityPositive", "[Quantity] > 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).HasColumnType("decimal(18,3)");

        builder.HasOne(l => l.Cart)
            .WithMany(c => c.Lines)
            .HasForeignKey(l => l.CartId)

            // The one place a cascade is right: cart lines have no meaning
            // without their cart, and neither is a financial record.
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.ProductVariant)
            .WithMany()
            .HasForeignKey(l => l.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // One line per variant. Adding the same shade twice raises the
        // quantity; two lines for it would let the basket show a total that
        // does not match what checkout builds.
        builder.HasIndex(l => new { l.CartId, l.ProductVariantId }).IsUnique();
    }
}
