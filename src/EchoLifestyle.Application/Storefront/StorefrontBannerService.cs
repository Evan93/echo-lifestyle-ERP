using EchoLifestyle.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Storefront;

/// <summary>
/// The banner the home page is showing right now.
///
/// Its own service rather than a method on the catalogue reader: a banner is
/// not a product and the rule for showing one has nothing to do with stock,
/// publication or price. Folding it into the catalogue would put two unrelated
/// visibility rules in one class, and that class is the one place the rule
/// about what customers may see has to stay readable in full.
/// </summary>
public class StorefrontBannerService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public StorefrontBannerService(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// How many banners the home page will rotate through.
    ///
    /// Three, and the cap is the point. Every slide after the first is seen by
    /// progressively fewer people, so a fourth costs a page-load's worth of
    /// image for an audience close to nobody. Three is enough to carry an
    /// offer, a delivery promise and a new arrival - which is genuinely three
    /// things worth saying - and few enough that a visitor can reach the end.
    /// </summary>
    public const int MaxSlides = 3;

    /// <summary>
    /// The banners the home page should show, in order. Empty is an ordinary
    /// answer, not a failure: a shop with nothing to announce should show its
    /// products, not an empty grey box where a banner would go.
    ///
    /// One is not a special case here. The view decides whether to draw arrows
    /// and start a rotation, and with a single slide it draws neither - so a
    /// shop running one banner gets exactly the static image it had before,
    /// with none of the carousel's weight.
    /// </summary>
    public async Task<IReadOnlyList<ShopBanner>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        return await _db.Banners
            .AsNoTracking()
            .Where(b => b.IsActive
                        && (b.StartsAtUtc == null || b.StartsAtUtc <= now)
                        && (b.EndsAtUtc == null || b.EndsAtUtc > now))
            .OrderBy(b => b.DisplayOrder)
            .ThenByDescending(b => b.Id)
            .Take(MaxSlides)
            .Select(b => new ShopBanner
            {
                ImagePath = b.ImagePath,
                MobileImagePath = b.MobileImagePath,
                AltText = b.AltText,
                Headline = b.Headline,
                Subheading = b.Subheading,
                LinkUrl = b.LinkUrl,
                ButtonText = b.ButtonText,
            })
            .ToListAsync(cancellationToken);
    }
}
