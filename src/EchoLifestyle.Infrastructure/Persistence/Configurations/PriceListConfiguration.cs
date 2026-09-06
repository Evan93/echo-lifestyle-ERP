using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ToTable("PriceLists", EchoDbContext.CatalogSchema);

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Kind).HasConversion<int>();
        builder.Property(p => p.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();

        builder.HasIndex(p => p.Code).IsUnique();

        // Exactly one default list, ever. A second one would make "the price"
        // depend on which row the query happened to return.
        builder.HasIndex(p => p.IsDefault)
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_PriceLists_SingleDefault");
    }
}

public class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> builder)
    {
        builder.ToTable("PriceListItems", EchoDbContext.CatalogSchema);

        builder.HasKey(i => i.Id);

        builder.Property(i => i.UnitPrice).HasColumnType("decimal(19,4)").IsRequired();

        builder.HasOne(i => i.PriceList)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PriceListId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ProductVariant)
            .WithMany(v => v.PriceListItems)
            .HasForeignKey(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one open price per variant per list. This is the constraint
        // that makes "the current price" a single unambiguous row rather than
        // whatever the service last happened to insert.
        builder.HasIndex(i => new { i.PriceListId, i.ProductVariantId })
            .IsUnique()
            .HasFilter("[EffectiveToUtc] IS NULL")
            .HasDatabaseName("UX_PriceListItems_OneOpenPricePerVariant");

        // "what was this priced at on 3 March" - the history read.
        builder.HasIndex(i => new { i.ProductVariantId, i.EffectiveFromUtc });
    }
}
