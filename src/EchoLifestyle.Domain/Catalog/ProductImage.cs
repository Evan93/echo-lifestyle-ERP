using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// A product photograph.
///
/// <see cref="ProductVariantId"/> being nullable is the whole point: images
/// with no variant are the general shots every shopper sees, and images with a
/// variant are that shade's own photographs. Selecting a shade on the
/// storefront swaps the gallery to that variant's images and falls back to the
/// product's when it has none.
/// </summary>
public class ProductImage : AuditableEntity
{
    public long ProductId { get; set; }

    public Product? Product { get; set; }

    public long? ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    /// <summary>
    /// Storage-relative path, never an absolute URL - the storage location
    /// changes when this moves behind a CDN, and stored absolute URLs would all
    /// have to be rewritten.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Required. An image with no alt text is invisible to search and unusable on a screen reader.</summary>
    public string AltText { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    /// <summary>
    /// The thumbnail used in listings. At most one per (product, variant)
    /// grouping.
    /// </summary>
    public bool IsPrimary { get; set; }
}
