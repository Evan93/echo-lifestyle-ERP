using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// A brand is a first-class entity rather than a text column on the product,
/// because a cosmetics storefront is browsed by brand at least as often as by
/// category: brand landing pages, brand filters and "shop by brand" rows all
/// need somewhere to hang a logo, a banner and a description.
/// </summary>
public class Brand : AuditableEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL segment. Unique among non-deleted brands and stable across renames,
    /// so a brand page never loses its address because someone fixed a typo in
    /// the display name.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Country of origin - shoppers filter on "Korean", "French".</summary>
    public string? OriginCountry { get; set; }

    public string? LogoPath { get; set; }

    public string? BannerPath { get; set; }

    /// <summary>Surfaced on the storefront home page.</summary>
    public bool IsFeatured { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
