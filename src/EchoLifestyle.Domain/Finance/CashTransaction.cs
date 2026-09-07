using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Common;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Purchasing;
using EchoLifestyle.Domain.Sales;

namespace EchoLifestyle.Domain.Finance;

/// <summary>
/// Every movement of money, in one append-only log.
///
/// The same shape as the stock ledger, for the same reason: a balance you can
/// edit is a balance nobody can explain. <see cref="SalesOrder.AmountCollected"/>
/// is a projection of the rows here, not a figure somebody types - so "why does
/// this order say it is paid?" always has an answer with a date and a name on
/// it.
///
/// Double-entry arrives with NBR registration in December. This log is what it
/// will be generated from: a journal is a projection over transactions like
/// these and can be produced backwards, whereas a thin cash summary cannot be
/// turned into one at all. That is why every row carries its party, its method
/// and its reference now, long before anything needs them.
/// </summary>
public class CashTransaction : AuditableEntity, IAppendOnly
{
    /// <summary>CT-yyMM-00001.</summary>
    public string Number { get; set; } = string.Empty;

    public CashDirection Direction { get; set; }

    public CashKind Kind { get; set; }

    public PaymentMethodKind Method { get; set; }

    /// <summary>
    /// Always positive. Direction says which way it went - a signed amount
    /// paired with a direction column is one bug waiting to happen, and it is a
    /// silent one.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Business date in Asia/Dhaka. What cash reports group by.</summary>
    public DateOnly TransactionDate { get; set; }

    /// <summary>
    /// The bKash transaction id, cheque number, or the courier's statement
    /// reference. The string somebody quotes when they say the money never
    /// arrived.
    /// </summary>
    public string? ReferenceNumber { get; set; }

    public long? SalesOrderId { get; set; }

    public SalesOrder? SalesOrder { get; set; }

    public long? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public long? SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    /// <summary>Required on an expense. What the money was spent on.</summary>
    public long? ExpenseCategoryId { get; set; }

    public ExpenseCategory? ExpenseCategory { get; set; }

    /// <summary>Required on capital and drawings. Whose money it was.</summary>
    public long? PartnerId { get; set; }

    public Partner? Partner { get; set; }

    /// <summary>The batch settlement this came in with, when it came from a courier.</summary>
    public long? CourierRemittanceId { get; set; }

    public CourierRemittance? CourierRemittance { get; set; }

    /// <summary>
    /// The entry this one cancels out.
    ///
    /// The only way to undo money in an append-only log. The reversal keeps the
    /// original's kind, category and party and flips only the direction, so
    /// every report that sums by category or by partner nets to the right
    /// figure without knowing reversals exist. A reversal that changed the kind
    /// to "adjustment" would leave the category permanently overstated.
    ///
    /// One reversal per entry, enforced by a filtered unique index.
    /// </summary>
    public long? ReversesCashTransactionId { get; set; }

    public CashTransaction? Reverses { get; set; }

    public long BranchId { get; set; }

    public Branch? Branch { get; set; }

    public string? Notes { get; set; }

    /// <summary>Positive in, negative out. What a running balance sums.</summary>
    public decimal SignedAmount => Direction == CashDirection.In ? Amount : -Amount;

    public bool IsReversal => ReversesCashTransactionId is not null;

    /// <summary>
    /// Which way this kind of money can only ever go, or null when it genuinely
    /// goes both ways.
    ///
    /// An expense is money out. Asking a user to pick that is asking them to get
    /// it wrong: nobody reads the direction box, and a wrongly-signed expense is
    /// invisible - the totals still add up, they are just wrong by twice the
    /// amount. So the kind decides, the form never asks, and the database
    /// refuses anything else.
    ///
    /// A reversal is the exception, and the only one. It carries its original's
    /// kind with the direction flipped, which is precisely what makes it a
    /// reversal.
    /// </summary>
    public static CashDirection? NaturalDirection(CashKind kind) => kind switch
    {
        CashKind.OrderCollection => CashDirection.In,
        CashKind.PartnerCapital => CashDirection.In,
        CashKind.Opening => CashDirection.In,

        CashKind.CustomerRefund => CashDirection.Out,
        CashKind.SupplierPayment => CashDirection.Out,
        CashKind.Expense => CashDirection.Out,
        CashKind.PartnerDrawing => CashDirection.Out,
        CashKind.CourierFee => CashDirection.Out,

        // A transfer is out of one method and into another; an adjustment
        // corrects in whichever direction the mistake went.
        _ => null,
    };

    /// <summary>An expense has to say what it was for.</summary>
    public static bool RequiresExpenseCategory(CashKind kind) => kind == CashKind.Expense;

    /// <summary>Capital and drawings have to say whose money it was.</summary>
    public static bool RequiresPartner(CashKind kind) =>
        kind is CashKind.PartnerCapital or CashKind.PartnerDrawing;

    /// <summary>A payment to a supplier has to say which supplier.</summary>
    public static bool RequiresSupplier(CashKind kind) => kind == CashKind.SupplierPayment;

    /// <summary>Money for an order has to say which order.</summary>
    public static bool RequiresSalesOrder(CashKind kind) =>
        kind is CashKind.OrderCollection or CashKind.CustomerRefund;
}

public enum CashDirection
{
    In = 1,
    Out = 2,
}

public enum CashKind
{
    /// <summary>A customer paid for an order, directly or through a courier.</summary>
    OrderCollection = 1,

    /// <summary>Money back to a customer.</summary>
    CustomerRefund = 2,

    SupplierPayment = 3,

    /// <summary>Rent, packaging, ads, the internet bill.</summary>
    Expense = 4,

    /// <summary>A partner putting money in.</summary>
    PartnerCapital = 5,

    /// <summary>A partner taking money out.</summary>
    PartnerDrawing = 6,

    /// <summary>
    /// What the courier kept. Deducted from the remittance rather than invoiced,
    /// which is why it is its own kind - it never appears as a bill to pay.
    /// </summary>
    CourierFee = 7,

    /// <summary>Cash that existed before this system did.</summary>
    Opening = 8,

    /// <summary>Between the till, bKash and the bank. Nets to nothing overall.</summary>
    Transfer = 9,

    /// <summary>A correction. Always paired with the entry it reverses.</summary>
    Adjustment = 10,
}

/// <summary>
/// How money physically moved.
///
/// Named to avoid colliding with <see cref="PaymentMethod"/> on an order, which
/// is what the customer agreed to. This is what actually happened, and the two
/// genuinely differ: an order taken as cash on delivery is very often settled by
/// a courier's bKash transfer.
/// </summary>
public enum PaymentMethodKind
{
    Cash = 1,
    Bkash = 2,
    Nagad = 3,
    BankTransfer = 4,
    Card = 5,
    Cheque = 6,

    /// <summary>No money moved; this row exists to correct another.</summary>
    Adjustment = 9,
}
