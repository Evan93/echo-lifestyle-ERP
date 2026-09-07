using EchoLifestyle.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", EchoDbContext.CrmSchema);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(200).IsRequired();

        // Exactly eleven, because it is stored normalised. A wider column would
        // invite somebody to write a raw "+880 1712-345678" straight into it.
        builder.Property(c => c.Phone).HasMaxLength(11).IsFixedLength().IsRequired();
        builder.Property(c => c.AlternatePhone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.CustomerType).HasConversion<int>();
        builder.Property(c => c.Source).HasConversion<int>();
        builder.Property(c => c.Notes).HasMaxLength(2000);
        builder.Property(c => c.BlockReason).HasMaxLength(500);

        builder.HasOne(c => c.PriceList)
            .WithMany()
            .HasForeignKey(c => c.PriceListId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.Code).IsUnique();

        // One customer per number, among the living. Filtered on IsDeleted so a
        // deleted customer's number can be used again by a real one - and so
        // that soft delete does not permanently burn a phone number.
        //
        // This index is the whole reason the number is normalised: without it,
        // the same person orders three times and becomes three customers with
        // no history between them.
        builder.HasIndex(c => c.Phone)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Customers_Phone");

        builder.HasIndex(c => c.FullName);

        // "Who is blocked?" - read before every order is confirmed.
        builder.HasIndex(c => c.IsBlocked).HasFilter("[IsBlocked] = 1");

        // The storefront account, when there is one. Filtered unique: one
        // customer record per login, and any number of customers with none.
        builder.HasIndex(c => c.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL")
            .HasDatabaseName("UX_Customers_UserId");
    }
}

public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses", EchoDbContext.CrmSchema);

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Label).HasMaxLength(50).IsRequired();
        builder.Property(a => a.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.RecipientPhone).HasMaxLength(11).IsFixedLength().IsRequired();
        builder.Property(a => a.AreaOrThana).HasMaxLength(150).IsRequired();
        builder.Property(a => a.AddressLine).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Landmark).HasMaxLength(200);
        builder.Property(a => a.PostCode).HasMaxLength(10);
        builder.Property(a => a.DeliveryNotes).HasMaxLength(500);

        builder.HasOne(a => a.Customer)
            .WithMany(c => c.Addresses)
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Division)
            .WithMany()
            .HasForeignKey(a => a.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.District)
            .WithMany()
            .HasForeignKey(a => a.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        // One default per customer. Two would make "where does this order go?"
        // depend on which row a query happened to find first.
        builder.HasIndex(a => a.CustomerId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("UX_CustomerAddresses_OneDefault");

        builder.HasIndex(a => a.CustomerId);
        builder.HasIndex(a => a.DistrictId);
    }
}

public class DivisionConfiguration : IEntityTypeConfiguration<Division>
{
    public void Configure(EntityTypeBuilder<Division> builder)
    {
        builder.ToTable("Divisions", EchoDbContext.CrmSchema);

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasMaxLength(60).IsRequired();
        builder.Property(d => d.NameBn).HasMaxLength(60);

        builder.HasIndex(d => d.Name).IsUnique();
    }
}

public class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("Districts", EchoDbContext.CrmSchema);

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasMaxLength(60).IsRequired();
        builder.Property(d => d.NameBn).HasMaxLength(60);
        builder.Property(d => d.FormerName).HasMaxLength(60);

        builder.HasOne(d => d.Division)
            .WithMany(v => v.Districts)
            .HasForeignKey(d => d.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // District names are unique nationally - there is one Cumilla - so the
        // index does not need the division in it, and a mis-parented district
        // fails loudly rather than becoming a second copy.
        builder.HasIndex(d => d.Name).IsUnique();

        builder.HasIndex(d => d.DivisionId);
    }
}
