using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// The stockable, sellable, purchasable thing. Stock ledger entries, price
/// list items, purchase lines, order lines and batches all reference a variant
/// and never a product.
///
/// A product with no meaningful variation still has exactly one variant, with
/// <see cref="IsDefault"/> set and no option values attached. The entry form
/// creates it silently.
/// </summary>
public class ProductVariant : AuditableEntity
{
    public long ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Unique across the whole catalogue, not just within a product.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// Scanned at the till. Unique where present; many variants legitimately
    /// have none, which is why the index is filtered rather than plain.
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// Cached join of the option values ("30ml / Ruby Red"), rebuilt whenever
    /// the variant's option values change. Denormalised on purpose: every grid,
    /// receipt line and picking list shows it, and none of them should pay for
    /// three joins to render a label.
    /// </summary>
    public string VariantName { get; set; } = string.Empty;

    /// <summary>
    /// Maximum retail price printed on the pack. Reference and compliance data,
    /// not the selling price - the selling price lives in the price list.
    /// </summary>
    public decimal? Mrp { get; set; }

    /// <summary>
    /// The "was" figure behind a struck-through price on the storefront. Stored
    /// rather than derived so a discount badge is a merchandising decision and
    /// not an accident of price history.
    /// </summary>
    public decimal? CompareAtPrice { get; set; }

    /// <summary>Used to apportion freight by weight on imported shipments.</summary>
    public decimal? WeightGrams { get; set; }

    /// <summary>
    /// Exactly one per product. Used when something needs a variant but the
    /// context has only a product - quick entry, imports, legacy references.
    /// </summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public ICollection<ProductVariantOptionValue> OptionValues { get; set; }
        = new List<ProductVariantOptionValue>();

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    public ICollection<PriceListItem> PriceListItems { get; set; } = new List<PriceListItem>();
}

/// <summary>
/// Ties a variant to its choice on one option. A variant has at most one row
/// per option, enforced by a unique index - without it a variant could claim to
/// be both 30ml and 50ml and every listing would show it twice.
/// </summary>
public class ProductVariantOptionValue : BaseEntity
{
    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public long ProductOptionId { get; set; }

    public ProductOption? ProductOption { get; set; }

    public long ProductOptionValueId { get; set; }

    public ProductOptionValue? ProductOptionValue { get; set; }
}
