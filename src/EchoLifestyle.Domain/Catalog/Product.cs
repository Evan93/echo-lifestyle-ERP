using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// The merchandising record: what a shopper thinks of as "a product".
///
/// A product is NOT stockable and NOT priced. Everything that has a quantity,
/// a cost or a price hangs off <see cref="ProductVariant"/> - including for
/// products that only ever have one variant. That single rule is what stops
/// every join in inventory, sales and purchasing from having to ask whether it
/// is pointing at a product or a variant.
/// </summary>
public class Product : AuditableEntity, ISoftDeletable
{
    /// <summary>Internal reference, unique across non-deleted products.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public long BrandId { get; set; }

    public Brand? Brand { get; set; }

    public long UnitOfMeasureId { get; set; }

    public UnitOfMeasure? UnitOfMeasure { get; set; }

    public string? ShortDescription { get; set; }

    /// <summary>Rich text from the editor. Sanitised on save, encoded on output.</summary>
    public string? LongDescription { get; set; }

    public string? HowToUse { get; set; }

    public string? Ingredients { get; set; }

    /// <summary>
    /// When true, receipts require a real batch number from the supplier.
    /// When false, receiving still creates a batch - it is just generated and
    /// never shown, so traceability exists without data entry.
    /// </summary>
    public bool IsBatchTracked { get; set; }

    /// <summary>Expiry is only prompted for when this is set.</summary>
    public bool IsExpiryTracked { get; set; }

    /// <summary>
    /// Used to suggest an expiry date at receipt when the supplier gives a
    /// manufacture date instead. Null means "ask, do not guess".
    /// </summary>
    public int? ShelfLifeDays { get; set; }

    /// <summary>
    /// Dormant until an NBR registration is entered; see CLAUDE.md rule 7.
    /// Null means the default rate applies once tax is switched on.
    /// </summary>
    public long? TaxCategoryId { get; set; }

    /// <summary>Usable in the ERP: purchasable, stockable, sellable in store.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Visible on the storefront. Deliberately separate from IsActive: stock
    /// arrives and is counted weeks before the product photography is ready.
    /// </summary>
    public bool IsPublished { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }

    public ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();

    public ICollection<ProductOption> Options { get; set; } = new List<ProductOption>();

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    /// <summary>
    /// Three is the point past which a variant matrix stops being enterable by
    /// a human and starts needing a bulk import.
    /// </summary>
    public const int MaxOptions = 3;
}
