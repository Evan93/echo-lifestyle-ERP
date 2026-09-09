using EchoLifestyle.Domain.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies", EchoDbContext.AdminSchema);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.LegalName).HasMaxLength(200);
        builder.Property(c => c.VatRegistrationNumber).HasMaxLength(50);
        builder.Property(c => c.TradeLicenseNumber).HasMaxLength(50);
        builder.Property(c => c.AddressLine1).HasMaxLength(250);
        builder.Property(c => c.AddressLine2).HasMaxLength(250);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.PostalCode).HasMaxLength(20);
        builder.Property(c => c.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(50);

        builder.Property(c => c.DeliveryChargeInsideCity).HasColumnType("decimal(19,4)");
        builder.Property(c => c.DeliveryChargeOutsideCity).HasColumnType("decimal(19,4)");
        builder.Property(c => c.FreeDeliveryOverAmount).HasColumnType("decimal(19,4)");
        builder.Property(c => c.Email).HasMaxLength(250);
        builder.Property(c => c.BaseCurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(c => c.BusinessTimeZoneId).HasMaxLength(100).IsRequired();

        builder.HasIndex(c => c.Name);
    }
}
