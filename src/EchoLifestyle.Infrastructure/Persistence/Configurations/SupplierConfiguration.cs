using EchoLifestyle.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", EchoDbContext.PurchasingSchema);

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ContactName).HasMaxLength(150);
        builder.Property(s => s.Phone).HasMaxLength(40);
        builder.Property(s => s.Email).HasMaxLength(200);
        builder.Property(s => s.AddressLine1).HasMaxLength(250);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.Country).HasMaxLength(100);
        builder.Property(s => s.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(s => s.Bin).HasMaxLength(50);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        // Filtered on IsDeleted so a removed supplier releases its code and its
        // name, matching how every other master record behaves here.
        builder.HasIndex(s => s.Code).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(s => s.Name).IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasIndex(s => new { s.IsActive, s.IsImporter });
    }
}
