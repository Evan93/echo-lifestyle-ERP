using EchoLifestyle.Domain.Marketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> builder)
    {
        builder.ToTable("Banners", EchoDbContext.MarketingSchema);

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name).HasMaxLength(120).IsRequired();
        builder.Property(b => b.Headline).HasMaxLength(200);
        builder.Property(b => b.Subheading).HasMaxLength(400);
        builder.Property(b => b.ImagePath).HasMaxLength(400).IsRequired();
        builder.Property(b => b.MobileImagePath).HasMaxLength(400);
        builder.Property(b => b.AltText).HasMaxLength(300).IsRequired();
        builder.Property(b => b.LinkUrl).HasMaxLength(500);
        builder.Property(b => b.ButtonText).HasMaxLength(60);

        // IsLiveAt is a method rather than a property on purpose: whether a
        // banner is live depends on the current time, which is not something a
        // column can hold. EF maps no method, so there is nothing to ignore -
        // and the storefront applies the same window in its query.

        // The exact order the storefront reads in, so choosing the live banner
        // is an index scan of a handful of rows rather than a sort.
        builder.HasIndex(b => new { b.IsActive, b.DisplayOrder });

        // A window that ends before it starts would show the banner never, and
        // silently. Refused at the database as well as in the service, because
        // this is the kind of rule that a future bulk import would otherwise
        // walk straight past.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Banners_WindowOrder",
            "[EndsAtUtc] IS NULL OR [StartsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]"));
    }
}
