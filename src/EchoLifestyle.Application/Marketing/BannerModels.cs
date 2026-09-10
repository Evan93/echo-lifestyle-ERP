namespace EchoLifestyle.Application.Marketing;

/// <summary>One banner in the back-office list.</summary>
public class BannerListItem
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Headline { get; set; }

    public string ImagePath { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    /// <summary>
    /// Whether this is the one the website is showing right now.
    ///
    /// Computed and shown in the list on purpose. With scheduling in play,
    /// "active" no longer means "live" - a banner can be ticked active and
    /// still be invisible because its window has not opened, and a list that
    /// did not say so would send somebody hunting for a bug that is not there.
    /// </summary>
    public bool IsLiveNow { get; set; }
}

public class BannerDetail
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Headline { get; set; }

    public string? Subheading { get; set; }

    public string ImagePath { get; set; } = string.Empty;

    public string? MobileImagePath { get; set; }

    public string AltText { get; set; } = string.Empty;

    public string? LinkUrl { get; set; }

    public string? ButtonText { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }
}

public class SaveBannerRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Headline { get; set; }

    public string? Subheading { get; set; }

    public string AltText { get; set; } = string.Empty;

    public string? LinkUrl { get; set; }

    public string? ButtonText { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }
}

// ShopBanner - what the storefront draws - deliberately lives with the other
// Shop* models in Application.Storefront rather than here. These types are the
// back office's view of a banner; that one is the customer's, and the two are
// allowed to diverge.
