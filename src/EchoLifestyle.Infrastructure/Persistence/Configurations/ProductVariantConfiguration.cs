using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants", EchoDbContext.CatalogSchema);

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Sku).HasMaxLength(40).IsRequired();
        builder.Property(v => v.Barcode).HasMaxLength(50);
        builder.Property(v => v.VariantName).HasMaxLength(300).IsRequired();
        builder.Property(v => v.Mrp).HasColumnType("decimal(19,4)");
        builder.Property(v => v.CompareAtPrice).HasColumnType("decimal(19,4)");
        builder.Property(v => v.WeightGrams).HasColumnType("decimal(18,3)");

        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deliberately NOT filtered on the product's IsDeleted: an SKU that has
        // ever held stock must never be reissued to a different item, or the
        // stock ledger becomes unreadable. Soft-deleting a product keeps its
        // SKUs reserved, and the service says so plainly when one collides.
        builder.HasIndex(v => v.Sku).IsUnique();

        // Most variants have no barcode, so the index has to be filtered - a
        // plain unique index would allow only one barcode-less variant in the
        // entire catalogue.
        builder.HasIndex(v => v.Barcode)
            .IsUnique()
            .HasFilter("[Barcode] IS NOT NULL");

        // Exactly one default variant per product: the one used when a context
        // has a product but needs something stockable.
        builder.HasIndex(v => v.ProductId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_ProductVariants_OneDefaultPerProduct");

        builder.HasIndex(v => new { v.ProductId, v.DisplayOrder });
    }
}

public class ProductVariantOptionValueConfiguration : IEntityTypeConfiguration<ProductVariantOptionValue>
{
    public void Configure(EntityTypeBuilder<ProductVariantOptionValue> builder)
    {
        builder.ToTable("ProductVariantOptionValues", EchoDbContext.CatalogSchema);

        builder.HasKey(x => x.Id);

        // All three are Restrict. Cascade would give SQL Server multiple
        // cascade paths back to Products through variant, option and value at
        // once, which it refuses outright; and catalogue rows are removed
        // explicitly by the editor anyway.
        builder.HasOne(x => x.ProductVariant)
            .WithMany(v => v.OptionValues)
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductOption)
            .WithMany()
            .HasForeignKey(x => x.ProductOptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductOptionValue)
            .WithMany(v => v.VariantOptionValues)
            .HasForeignKey(x => x.ProductOptionValueId)
            .OnDelete(DeleteBehavior.Restrict);

        // One answer per axis. Without this a variant could claim to be both
        // 30ml and 50ml, and would appear twice in every filtered listing.
        builder.HasIndex(x => new { x.ProductVariantId, x.ProductOptionId }).IsUnique();

        // "which variant is the Ruby Red one" - the storefront's swatch click.
        builder.HasIndex(x => x.ProductOptionValueId);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages", EchoDbContext.CatalogSchema);

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Path).HasMaxLength(400).IsRequired();
        builder.Property(i => i.AltText).HasMaxLength(250).IsRequired();

        builder.HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ProductVariant)
            .WithMany(v => v.Images)
            .HasForeignKey(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // One primary per gallery, where the product-level gallery (variant
        // null) counts as its own group. SQL Server treats those NULLs as
        // equal, which is the behaviour wanted here.
        builder.HasIndex(i => new { i.ProductId, i.ProductVariantId })
            .IsUnique()
            .HasFilter("[IsPrimary] = 1")
            .HasDatabaseName("UX_ProductImages_OnePrimaryPerGallery");

        builder.HasIndex(i => new { i.ProductId, i.DisplayOrder });
    }
}
