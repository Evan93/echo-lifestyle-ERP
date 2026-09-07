using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Common;
using EchoLifestyle.Domain.Sales;

namespace EchoLifestyle.Domain.Finance;

/// <summary>
/// A courier settling up.
///
/// This is the shape of the actual problem. Money does not arrive per order: a
/// courier holds forty parcels' worth of cash for a week and then sends one
/// transfer, net of their own fee, with a statement listing consignment numbers.
/// Somebody has to say which orders that lump sum covered, which parcels came
/// back instead, and why the number that landed is smaller than the total
/// collected.
///
/// Recording it as one document rather than forty separate payments is what
/// makes that reconcilable. The transfer either explains itself or it does not,
/// and a mismatch is caught here rather than discovered at year end.
/// </summary>
public class CourierRemittance : AuditableEntity
{
    /// <summary>REM-yyMM-0001.</summary>
    public string Number { get; set; } = string.Empty;

    public string CourierName { get; set; } = string.Empty;

    /// <summary>The day the money arrived, not the day the parcels were delivered.</summary>
    public DateOnly RemittanceDate { get; set; }

    /// <summary>The courier's own statement or payout reference.</summary>
    public string? StatementReference { get; set; }

    public CourierRemittanceStatus Status { get; set; } = CourierRemittanceStatus.Draft;

    public PaymentMethodKind ReceivedVia { get; set; } = PaymentMethodKind.Bkash;

    public long BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>Sum of what the courier says it collected, across every line.</summary>
    public decimal GrossCollected { get; set; }

    /// <summary>
    /// What the courier kept: delivery charges, COD handling, return fees. It is
    /// a real cost of selling and is recorded as money out, not netted away
    /// silently - otherwise the cost of delivery never appears anywhere.
    /// </summary>
    public decimal CourierFee { get; set; }

    /// <summary>Damage claims, adjustments, anything else they took off.</summary>
    public decimal OtherDeduction { get; set; }

    /// <summary>
    /// What actually landed. Typed by the person reading the bank or bKash
    /// message, then checked against gross minus deductions - a difference means
    /// the statement and the money disagree, and that is worth stopping for.
    /// </summary>
    public decimal NetReceived { get; set; }

    public string? Notes { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public long? PostedByUserId { get; set; }

    public ICollection<CourierRemittanceLine> Lines { get; set; } =
        new List<CourierRemittanceLine>();

    /// <summary>What the arithmetic says should have arrived.</summary>
    public decimal ExpectedNet => GrossCollected - CourierFee - OtherDeduction;

    /// <summary>
    /// Between what the statement adds up to and what the bank says. Zero is the
    /// only comfortable answer.
    /// </summary>
    public decimal Discrepancy => NetReceived - ExpectedNet;

    public bool Balances => Discrepancy == 0m;
}

public enum CourierRemittanceStatus
{
    /// <summary>Being reconciled. No money recorded, no order touched.</summary>
    Draft = 1,

    /// <summary>
    /// Posted. Cash transactions written, orders settled, returned parcels back
    /// on the shelf. Immutable from here.
    /// </summary>
    Posted = 2,

    Cancelled = 3,
}

/// <summary>
/// One order on the courier's statement.
///
/// Either they collected for it, or the parcel came back. Both are outcomes the
/// statement reports, and both have to be recorded from the same document -
/// splitting returns into a separate process is how the two stop adding up.
/// </summary>
public class CourierRemittanceLine : BaseEntity
{
    public long CourierRemittanceId { get; set; }

    public CourierRemittance? CourierRemittance { get; set; }

    public long SalesOrderId { get; set; }

    public SalesOrder? SalesOrder { get; set; }

    /// <summary>
    /// What the courier says it collected for this order. Often the full order
    /// total; sometimes short, when a customer haggled at the door and the rider
    /// took what was offered.
    /// </summary>
    public decimal AmountCollected { get; set; }

    /// <summary>True when the parcel came back instead of being delivered.</summary>
    public bool IsReturned { get; set; }

    public string? ReturnReason { get; set; }

    public string? Notes { get; set; }
}
