using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Marketing;

/// <summary>
/// The image across the top of the home page.
///
/// One shows at a time. The table holds many because the useful thing is not
/// having several on screen - it is having next month's Eid banner prepared and
/// dated, and last month's kept rather than deleted so it can be put back.
///
/// A rotating carousel was considered and rejected: almost nobody sees the
/// second slide, and on a phone it pushes the products themselves below the
/// fold. One banner that can be swapped in a minute does the same job without
/// the cost.
/// </summary>
public class Banner : AuditableEntity
{
    /// <summary>
    /// What it is called in the back office. Never shown to a customer - it is
    /// there so a list of six banners is readable ("Eid 2027", "Winter sale").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The large line over the image. Optional: some artwork carries its own.</summary>
    public string? Headline { get; set; }

    public string? Subheading { get; set; }

    /// <summary>The wide image, for a desktop screen.</summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// A taller crop for phones. Optional, and worth uploading: most traffic
    /// here is mobile, and a 3:1 desktop banner shown on a phone is either a
    /// letterbox strip too small to read or a centre crop that cuts the product
    /// out of its own advertisement.
    /// </summary>
    public string? MobileImagePath { get; set; }

    /// <summary>
    /// Describes the picture for somebody who cannot see it, and for a search
    /// engine. Required, because an undescribed image is the most common
    /// accessibility failure on a shop's home page.
    /// </summary>
    public string AltText { get; set; } = string.Empty;

    /// <summary>Where the banner goes when clicked. Optional - some just announce.</summary>
    public string? LinkUrl { get; set; }

    public string? ButtonText { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The window it is allowed to show in. Both null means "whenever it is
    /// active", which is the ordinary case.
    ///
    /// Scheduling rather than a switch somebody has to remember to flip at
    /// midnight: a sale banner that is still up a week after the sale ended is
    /// worse than no banner, because it advertises a price that is no longer
    /// honoured.
    /// </summary>
    public DateTime? StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    /// <summary>Whether this banner may show at the given moment.</summary>
    public bool IsLiveAt(DateTime utcNow) =>
        IsActive
        && (StartsAtUtc is null || StartsAtUtc <= utcNow)
        && (EndsAtUtc is null || EndsAtUtc > utcNow);
}
