using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// An axis along which a product varies - "Size", "Shade". Defined per product
/// rather than globally, because the same word means different things to
/// different products and a shared list would force every lipstick to share a
/// shade vocabulary with every foundation.
/// </summary>
public class ProductOption : BaseEntity
{
    public long ProductId { get; set; }

    public Product? Product { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Drives the order of the selectors on the product page and the order of
    /// the parts in a variant's display name.
    /// </summary>
    public int DisplayOrder { get; set; }

    public ICollection<ProductOptionValue> Values { get; set; } = new List<ProductOptionValue>();
}

/// <summary>
/// One choice on an axis: "30ml", "Ruby Red".
/// </summary>
public class ProductOptionValue : BaseEntity
{
    public long ProductOptionId { get; set; }

    public ProductOption? ProductOption { get; set; }

    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Hex colour for shade swatches (#RRGGBB). Null for non-colour axes.
    /// This one nullable field is the difference between a storefront that
    /// shows shade circles and one that shows a dropdown.
    /// </summary>
    public string? SwatchHex { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<ProductVariantOptionValue> VariantOptionValues { get; set; }
        = new List<ProductVariantOptionValue>();
}
