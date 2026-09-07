using EchoLifestyle.Domain.Finance;

namespace EchoLifestyle.Application.Finance.Cash;

/// <summary>
/// One movement of money, as a screen asks for it.
///
/// Direction is absent on purpose. The kind decides it - see
/// <see cref="CashTransaction.NaturalDirection"/> - so there is no box for
/// somebody to tick the wrong way round.
/// </summary>
public class RecordCashRequest
{
    public CashKind Kind { get; set; } = CashKind.Expense;

    public PaymentMethodKind Method { get; set; } = PaymentMethodKind.Cash;

    public decimal Amount { get; set; }

    /// <summary>Null means today, in Dhaka.</summary>
    public DateOnly? TransactionDate { get; set; }

    public long BranchId { get; set; }

    public long? ExpenseCategoryId { get; set; }

    public long? PartnerId { get; set; }

    public long? SupplierId { get; set; }

    /// <summary>
    /// The order this settles or refunds, by its number rather than its id, so
    /// somebody can type what is on the parcel.
    /// </summary>
    public string? OrderNumber { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }
}

/// <summary>A row in the money log, as a grid shows it.</summary>
public class CashListItem
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateOnly TransactionDate { get; set; }

    public CashKind Kind { get; set; }

    public CashDirection Direction { get; set; }

    public PaymentMethodKind Method { get; set; }

    public decimal Amount { get; set; }

    public decimal SignedAmount => Direction == CashDirection.In ? Amount : -Amount;

    /// <summary>Category, partner, supplier or customer - whoever it concerns.</summary>
    public string? Party { get; set; }

    public string? OrderNumber { get; set; }

    public long? SalesOrderId { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? Notes { get; set; }

    public bool IsReversal { get; set; }

    /// <summary>True once something has cancelled this entry out.</summary>
    public bool IsReversed { get; set; }

    /// <summary>Set on rows a courier payout wrote; those are not hand-editable.</summary>
    public long? CourierRemittanceId { get; set; }
}

/// <summary>One entry, in full.</summary>
public class CashDetail : CashListItem
{
    public string BranchName { get; set; } = string.Empty;

    public string? ExpenseCategoryName { get; set; }

    public string? PartnerName { get; set; }

    public string? SupplierName { get; set; }

    public string? CustomerName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedByName { get; set; }

    /// <summary>The entry this one cancels, when it is a reversal.</summary>
    public string? ReversesNumber { get; set; }

    public long? ReversesId { get; set; }

    /// <summary>The reversal that cancelled this one, when there is one.</summary>
    public string? ReversedByNumber { get; set; }

    public long? ReversedById { get; set; }
}

/// <summary>
/// What is in each pot, over a period.
///
/// Opening plus in minus out equals closing, per payment method. The figure a
/// business actually checks against the cash box and the bKash balance - and
/// the only reason a mistyped direction ever gets found.
/// </summary>
public class CashPosition
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public IReadOnlyList<CashPositionRow> Rows { get; set; } = [];

    public decimal Opening => Rows.Sum(r => r.Opening);

    public decimal In => Rows.Sum(r => r.In);

    public decimal Out => Rows.Sum(r => r.Out);

    public decimal Closing => Rows.Sum(r => r.Closing);

    public decimal Net => In - Out;
}

public class CashPositionRow
{
    public PaymentMethodKind Method { get; set; }

    /// <summary>Everything before <see cref="CashPosition.From"/>, netted.</summary>
    public decimal Opening { get; set; }

    public decimal In { get; set; }

    public decimal Out { get; set; }

    public decimal Closing => Opening + In - Out;

    public int Entries { get; set; }
}

/// <summary>Where the money went, over a period.</summary>
public class ExpenseBreakdown
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public IReadOnlyList<ExpenseBreakdownRow> Rows { get; set; } = [];

    public decimal Total => Rows.Sum(r => r.Amount);

    /// <summary>Costs that rise with volume - packaging, courier charges.</summary>
    public decimal CostOfSale => Rows.Where(r => r.IsCostOfSale).Sum(r => r.Amount);

    /// <summary>The cost of being open at all.</summary>
    public decimal Operating => Total - CostOfSale;
}

public class ExpenseBreakdownRow
{
    public long? ExpenseCategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public bool IsCostOfSale { get; set; }

    /// <summary>Net of any reversals, which is why it is summed signed.</summary>
    public decimal Amount { get; set; }

    public int Entries { get; set; }

    public decimal ShareOfTotal { get; set; }
}

/// <summary>What each partner has put in and taken out.</summary>
public class PartnerLedgerRow
{
    public long PartnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal? OwnershipPercent { get; set; }

    public bool IsActive { get; set; }

    public decimal CapitalIn { get; set; }

    public decimal Drawings { get; set; }

    /// <summary>
    /// What the business owes them on this account. Not their share of the
    /// business - profit is not distributed here - just money in minus money
    /// out.
    /// </summary>
    public decimal Balance => CapitalIn - Drawings;

    public DateOnly? LastMovement { get; set; }
}

/// <summary>A category, as a dropdown needs it.</summary>
public class ExpenseCategoryOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsCostOfSale { get; set; }
}

/// <summary>A partner, as a dropdown needs it.</summary>
public class PartnerOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
