using EchoLifestyle.Domain.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches", EchoDbContext.AdminSchema);

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.Type).HasConversion<int>();
        builder.Property(b => b.AddressLine1).HasMaxLength(250);
        builder.Property(b => b.City).HasMaxLength(100);
        builder.Property(b => b.Phone).HasMaxLength(50);

        builder.HasOne(b => b.Company)
            .WithMany(c => c.Branches)
            .HasForeignKey(b => b.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Branch codes appear on documents, so they must be unique per company.
        // Filtered so a retired branch's code can be reused.
        builder.HasIndex(b => new { b.CompanyId, b.Code })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
    }
}
