using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitsOfMeasure", EchoDbContext.CatalogSchema);

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Code).HasMaxLength(10).IsRequired();
        builder.Property(u => u.Name).HasMaxLength(50).IsRequired();

        builder.HasIndex(u => u.Code).IsUnique();
    }
}

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("Brands", EchoDbContext.CatalogSchema);

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(150).IsRequired();
        builder.Property(b => b.Slug).HasMaxLength(160).IsRequired();
        builder.Property(b => b.Description).HasMaxLength(4000);
        builder.Property(b => b.OriginCountry).HasMaxLength(100);
        builder.Property(b => b.LogoPath).HasMaxLength(400);
        builder.Property(b => b.BannerPath).HasMaxLength(400);

        // Both filtered on IsDeleted so that a removed brand releases its name
        // and its URL for reuse.
        builder.HasIndex(b => b.Name).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(b => b.Slug).IsUnique().HasFilter("[IsDeleted] = 0");

        // The storefront's "featured brands" row, ordered.
        builder.HasIndex(b => new { b.IsActive, b.IsFeatured, b.DisplayOrder });
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", EchoDbContext.CatalogSchema);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(160).IsRequired();
        builder.Property(c => c.Path).HasMaxLength(450).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(4000);
        builder.Property(c => c.ImagePath).HasMaxLength(400);

        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Slugs are unique among siblings, not globally: "cleansers" belongs
        // under both Skincare and Haircare and means something different in
        // each. SQL Server treats NULL parents as equal here, which is exactly
        // what is wanted - two roots may not share a slug.
        builder.HasIndex(c => new { c.ParentId, c.Slug })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // The index that makes "everything under this node" a prefix seek
        // rather than a recursive walk.
        builder.HasIndex(c => c.Path);

        builder.HasIndex(c => new { c.ParentId, c.DisplayOrder });
    }
}
