using EchoLifestyle.Domain.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses", EchoDbContext.AdminSchema);

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Code).HasMaxLength(20).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Kind).HasConversion<int>();
        builder.Property(w => w.AddressLine1).HasMaxLength(250);
        builder.Property(w => w.City).HasMaxLength(100);

        builder.HasOne(w => w.Company)
            .WithMany()
            .HasForeignKey(w => w.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(w => new { w.CompanyId, w.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
