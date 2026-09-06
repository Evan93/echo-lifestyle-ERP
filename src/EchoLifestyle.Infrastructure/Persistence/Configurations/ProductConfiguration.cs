using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", EchoDbContext.CatalogSchema);

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(250).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(160).IsRequired();
        builder.Property(p => p.ShortDescription).HasMaxLength(600);
        builder.Property(p => p.LongDescription).HasMaxLength(8000);
        builder.Property(p => p.HowToUse).HasMaxLength(4000);
        builder.Property(p => p.Ingredients).HasMaxLength(8000);

        builder.HasOne(p => p.Brand)
            .WithMany(b => b.Products)
            .HasForeignKey(p => p.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.UnitOfMeasure)
            .WithMany()
            .HasForeignKey(p => p.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Code).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(p => p.Slug).IsUnique().HasFilter("[IsDeleted] = 0");

        // The storefront's listing query: published products of a brand.
        builder.HasIndex(p => new { p.IsPublished, p.BrandId });
        builder.HasIndex(p => p.IsActive);
    }
}

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("ProductCategories", EchoDbContext.CatalogSchema);

        builder.HasKey(pc => pc.Id);

        builder.HasOne(pc => pc.Product)
            .WithMany(p => p.ProductCategories)
            .HasForeignKey(pc => pc.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pc => pc.Category)
            .WithMany(c => c.ProductCategories)
            .HasForeignKey(pc => pc.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pc => new { pc.ProductId, pc.CategoryId }).IsUnique();

        // At most one primary category per product. Without this, "sales by
        // category" would double-count anything filed in three places, and the
        // breadcrumb would be whichever row came back first.
        builder.HasIndex(pc => pc.ProductId)
            .IsUnique()
            .HasFilter("[IsPrimary] = 1")
            .HasDatabaseName("UX_ProductCategories_OnePrimaryPerProduct");

        // Category listing pages read this way round.
        builder.HasIndex(pc => pc.CategoryId);
    }
}

public class ProductOptionConfiguration : IEntityTypeConfiguration<ProductOption>
{
    public void Configure(EntityTypeBuilder<ProductOption> builder)
    {
        builder.ToTable("ProductOptions", EchoDbContext.CatalogSchema);

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(50).IsRequired();

        builder.HasOne(o => o.Product)
            .WithMany(p => p.Options)
            .HasForeignKey(o => o.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(o => new { o.ProductId, o.Name }).IsUnique();
    }
}

public class ProductOptionValueConfiguration : IEntityTypeConfiguration<ProductOptionValue>
{
    public void Configure(EntityTypeBuilder<ProductOptionValue> builder)
    {
        builder.ToTable("ProductOptionValues", EchoDbContext.CatalogSchema);

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Value).HasMaxLength(100).IsRequired();

        // "#RRGGBB" - seven characters, validated in the service before it gets
        // this far.
        builder.Property(v => v.SwatchHex).HasMaxLength(7).IsFixedLength(false);

        builder.HasOne(v => v.ProductOption)
            .WithMany(o => o.Values)
            .HasForeignKey(v => v.ProductOptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(v => new { v.ProductOptionId, v.Value }).IsUnique();
    }
}
