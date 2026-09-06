using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// A named set of selling prices. Exactly one retail list exists today and it
/// is the default; wholesale arrives without a schema change.
///
/// The reason this is a table rather than a column on the variant is not
/// wholesale - it is history. Margin reporting has to answer "what did this
/// sell for in March", and a mutable price column cannot.
/// </summary>
public class PriceList : AuditableEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PriceListKind Kind { get; set; } = PriceListKind.Retail;

    /// <summary>BDT only for now; the column exists so that changes later are data, not schema.</summary>
    public string CurrencyCode { get; set; } = "BDT";

    /// <summary>
    /// The list a price goes to when nobody chose one. Exactly one row may have
    /// this set, enforced by a filtered unique index.
    /// </summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<PriceListItem> Items { get; set; } = new List<PriceListItem>();
}

public enum PriceListKind
{
    Retail = 1,
    Wholesale = 2,
}

/// <summary>
/// One variant's price on one list, over one period.
///
/// Prices are never updated in place. Changing a price closes the current row
/// by stamping <see cref="EffectiveToUtc"/> and inserts a new one, so the
/// price a past order was placed at is still answerable.
/// </summary>
public class PriceListItem : AuditableEntity
{
    public long PriceListId { get; set; }

    public PriceList? PriceList { get; set; }

    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public decimal UnitPrice { get; set; }

    public DateTime EffectiveFromUtc { get; set; }

    /// <summary>
    /// Null means "current". At most one open row per (list, variant), enforced
    /// by a filtered unique index rather than by hoping the service behaves.
    /// </summary>
    public DateTime? EffectiveToUtc { get; set; }
}
