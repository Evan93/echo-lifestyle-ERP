using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Purchasing;

/// <summary>
/// Somebody the business buys from.
///
/// <see cref="IsImporter"/> is the field that keeps the purchase screen honest:
/// buying from a local distributor is three fields, and freight, duty, clearing
/// and an exchange rate only appear when the supplier is actually an import
/// source. Asking every purchase for customs paperwork would make the common
/// case pay for the rare one.
/// </summary>
public class Supplier : AuditableEntity, ISoftDeletable
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? AddressLine1 { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    /// <summary>
    /// Turns on the landed-cost fields at receipt. Purchases are still recorded
    /// and posted in BDT; the currency and rate are captured on the document so
    /// the true unit cost can be worked out.
    /// </summary>
    public bool IsImporter { get; set; }

    /// <summary>Invoice currency. BDT for local suppliers.</summary>
    public string CurrencyCode { get; set; } = "BDT";

    /// <summary>Days from invoice to payment. 0 means cash on purchase.</summary>
    public int PaymentTermDays { get; set; }

    /// <summary>Business Identification Number, once the supplier gives one.</summary>
    public string? Bin { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }
}
