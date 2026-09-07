using EchoLifestyle.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoLifestyle.Infrastructure.Persistence.Configurations;

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("ExpenseCategories", EchoDbContext.FinanceSchema);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(300);

        // Two categories with the same name split every total between them, and
        // nobody notices until the year-end figure is wrong by half.
        builder.HasIndex(c => c.Name).IsUnique();

        builder.HasIndex(c => new { c.IsActive, c.DisplayOrder });
    }
}

public class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("Partners", EchoDbContext.FinanceSchema, table =>
        {
            table.HasCheckConstraint(
                "CK_Partners_OwnershipPercentInRange",
                "[OwnershipPercent] IS NULL OR ([OwnershipPercent] >= 0 AND [OwnershipPercent] <= 100)");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.Property(p => p.OwnershipPercent).HasColumnType("decimal(5,2)");

        builder.HasIndex(p => p.Name).IsUnique();

        // A plain scalar, not a navigation. Finance has no business holding a
        // reference into Identity, and a partner outlives the account.
        builder.HasIndex(p => p.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
    }
}

public class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> builder)
    {
        builder.ToTable("CashTransactions", EchoDbContext.FinanceSchema, table =>
        {
            // A movement of nothing is not a movement. Allowing zero would fill
            // the log with rows that mean nothing and hide the bug that made
            // them.
            table.HasCheckConstraint("CK_CashTransactions_AmountPositive", "[Amount] > 0");

            // An expense with no category is a number nobody can act on; capital
            // with no partner is money from nowhere. The form asks for these, and
            // this is what stops a second write path forgetting to.
            table.HasCheckConstraint(
                "CK_CashTransactions_ExpenseHasCategory",
                "[Kind] <> 4 OR [ExpenseCategoryId] IS NOT NULL");

            table.HasCheckConstraint(
                "CK_CashTransactions_PartnerMovementHasPartner",
                "[Kind] NOT IN (5, 6) OR [PartnerId] IS NOT NULL");

            table.HasCheckConstraint(
                "CK_CashTransactions_SupplierPaymentHasSupplier",
                "[Kind] <> 3 OR [SupplierId] IS NOT NULL");

            // An expense is money out; capital is money in. A wrongly signed
            // entry is invisible - the totals still add up, they are just wrong
            // by twice the amount - so the database refuses it outright.
            //
            // A reversal is the one exception, and carrying its original's kind
            // with the direction flipped is exactly what makes it one.
            table.HasCheckConstraint(
                "CK_CashTransactions_DirectionMatchesKind",
                "[ReversesCashTransactionId] IS NOT NULL "
                + "OR ([Kind] IN (1, 5, 8) AND [Direction] = 1) "
                + "OR ([Kind] IN (2, 3, 4, 6, 7) AND [Direction] = 2) "
                + "OR [Kind] IN (9, 10)");

            // Nothing reverses itself.
            table.HasCheckConstraint(
                "CK_CashTransactions_NotSelfReversing",
                "[ReversesCashTransactionId] IS NULL OR [ReversesCashTransactionId] <> [Id]");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Number).HasMaxLength(40).IsRequired();
        builder.Property(t => t.Direction).HasConversion<int>();
        builder.Property(t => t.Kind).HasConversion<int>();
        builder.Property(t => t.Method).HasConversion<int>();
        builder.Property(t => t.Amount).HasColumnType("decimal(19,4)");
        builder.Property(t => t.ReferenceNumber).HasMaxLength(100);
        builder.Property(t => t.Notes).HasMaxLength(500);

        // Derived from two stored columns; a stored copy is one more thing that
        // can disagree with them.
        builder.Ignore(t => t.SignedAmount);
        builder.Ignore(t => t.IsReversal);

        builder.HasOne(t => t.SalesOrder)
            .WithMany()
            .HasForeignKey(t => t.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Customer)
            .WithMany()
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Supplier)
            .WithMany()
            .HasForeignKey(t => t.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ExpenseCategory)
            .WithMany()
            .HasForeignKey(t => t.ExpenseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Partner)
            .WithMany()
            .HasForeignKey(t => t.PartnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.CourierRemittance)
            .WithMany()
            .HasForeignKey(t => t.CourierRemittanceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Reverses)
            .WithMany()
            .HasForeignKey(t => t.ReversesCashTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Branch)
            .WithMany()
            .HasForeignKey(t => t.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.Number).IsUnique();

        // "What has this order been paid?" - the query behind every collected
        // figure on every screen.
        builder.HasIndex(t => t.SalesOrderId).HasFilter("[SalesOrderId] IS NOT NULL");

        // "What came in this month?" - every cash report.
        builder.HasIndex(t => new { t.TransactionDate, t.Direction });

        builder.HasIndex(t => t.CourierRemittanceId)
            .HasFilter("[CourierRemittanceId] IS NOT NULL");

        // "What did we spend on packaging in July?" - the expense report, and
        // the one query that has to stay fast as the log grows.
        builder.HasIndex(t => new { t.ExpenseCategoryId, t.TransactionDate })
            .HasFilter("[ExpenseCategoryId] IS NOT NULL");

        builder.HasIndex(t => new { t.PartnerId, t.TransactionDate })
            .HasFilter("[PartnerId] IS NOT NULL");

        // One reversal per entry. Without this, reversing twice halves a figure
        // that was only ever wrong once, and the log still looks consistent.
        builder.HasIndex(t => t.ReversesCashTransactionId)
            .IsUnique()
            .HasFilter("[ReversesCashTransactionId] IS NOT NULL");

        // The cash position screen: everything before a date, grouped by method.
        builder.HasIndex(t => new { t.TransactionDate, t.Method });
    }
}

public class CourierRemittanceConfiguration : IEntityTypeConfiguration<CourierRemittance>
{
    public void Configure(EntityTypeBuilder<CourierRemittance> builder)
    {
        builder.ToTable("CourierRemittances", EchoDbContext.FinanceSchema, table =>
        {
            table.HasCheckConstraint(
                "CK_CourierRemittances_AmountsNotNegative",
                "[GrossCollected] >= 0 AND [CourierFee] >= 0 AND [OtherDeduction] >= 0 "
                + "AND [NetReceived] >= 0");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Number).HasMaxLength(40).IsRequired();
        builder.Property(r => r.CourierName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.StatementReference).HasMaxLength(100);
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.ReceivedVia).HasConversion<int>();
        builder.Property(r => r.GrossCollected).HasColumnType("decimal(19,4)");
        builder.Property(r => r.CourierFee).HasColumnType("decimal(19,4)");
        builder.Property(r => r.OtherDeduction).HasColumnType("decimal(19,4)");
        builder.Property(r => r.NetReceived).HasColumnType("decimal(19,4)");
        builder.Property(r => r.Notes).HasMaxLength(1000);

        builder.Ignore(r => r.ExpectedNet);
        builder.Ignore(r => r.Discrepancy);
        builder.Ignore(r => r.Balances);

        builder.HasOne(r => r.Branch)
            .WithMany()
            .HasForeignKey(r => r.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.Number).IsUnique();
        builder.HasIndex(r => new { r.Status, r.RemittanceDate });
        builder.HasIndex(r => new { r.CourierName, r.RemittanceDate });
    }
}

public class CourierRemittanceLineConfiguration : IEntityTypeConfiguration<CourierRemittanceLine>
{
    public void Configure(EntityTypeBuilder<CourierRemittanceLine> builder)
    {
        builder.ToTable("CourierRemittanceLines", EchoDbContext.FinanceSchema, table =>
        {
            table.HasCheckConstraint(
                "CK_CourierRemittanceLines_CollectedNotNegative",
                "[AmountCollected] >= 0");

            // A returned parcel collected nothing. Recording both would mean the
            // statement says the customer paid for a box that came back.
            table.HasCheckConstraint(
                "CK_CourierRemittanceLines_ReturnedCollectsNothing",
                "[IsReturned] = 0 OR [AmountCollected] = 0");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.AmountCollected).HasColumnType("decimal(19,4)");
        builder.Property(l => l.ReturnReason).HasMaxLength(500);
        builder.Property(l => l.Notes).HasMaxLength(500);

        builder.HasOne(l => l.CourierRemittance)
            .WithMany(r => r.Lines)
            .HasForeignKey(l => l.CourierRemittanceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.SalesOrder)
            .WithMany()
            .HasForeignKey(l => l.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // One order per statement. The same order settled twice on one document
        // would be paid twice, and the totals would still add up.
        builder.HasIndex(l => new { l.CourierRemittanceId, l.SalesOrderId }).IsUnique();

        builder.HasIndex(l => l.SalesOrderId);
    }
}
