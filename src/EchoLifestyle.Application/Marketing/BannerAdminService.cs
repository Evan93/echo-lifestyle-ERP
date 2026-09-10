using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Marketing;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Marketing;

/// <summary>
/// Managing the home page banner.
///
/// The image is uploaded through <see cref="IFileStorage"/>, which chooses the
/// file name from content it has verified - so nothing a browser sends decides
/// where a file lands or what it is called.
/// </summary>
public class BannerAdminService
{
    /// <summary>
    /// Enough for a year of campaigns kept for reuse. Past this the list stops
    /// being something anybody reads.
    /// </summary>
    public const int MaxBanners = 30;

    private readonly IApplicationDbContext _db;
    private readonly IFileStorage _files;
    private readonly IDateTimeProvider _clock;

    public BannerAdminService(
        IApplicationDbContext db,
        IFileStorage files,
        IDateTimeProvider clock)
    {
        _db = db;
        _files = files;
        _clock = clock;
    }

    public async Task<IReadOnlyList<BannerListItem>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var rows = await _db.Banners
            .AsNoTracking()
            .OrderBy(b => b.DisplayOrder)
            .ThenByDescending(b => b.Id)
            .Select(b => new
            {
                b.Id,
                b.Name,
                b.Headline,
                b.ImagePath,
                b.IsActive,
                b.DisplayOrder,
                b.StartsAtUtc,
                b.EndsAtUtc,
            })
            .ToListAsync(cancellationToken);

        // Which banners are live is decided the same way the storefront decides
        // it - the first few in order among those whose window is open, capped
        // at the same number - rather than by a second rule that could quietly
        // disagree with the website.
        var liveIds = rows
            .Where(r => r.IsActive
                        && (r.StartsAtUtc is null || r.StartsAtUtc <= now)
                        && (r.EndsAtUtc is null || r.EndsAtUtc > now))
            .Take(Storefront.StorefrontBannerService.MaxSlides)
            .Select(r => r.Id)
            .ToHashSet();

        return rows.Select(r => new BannerListItem
        {
            Id = r.Id,
            Name = r.Name,
            Headline = r.Headline,
            ImagePath = r.ImagePath,
            IsActive = r.IsActive,
            DisplayOrder = r.DisplayOrder,
            StartsAtUtc = r.StartsAtUtc,
            EndsAtUtc = r.EndsAtUtc,
            IsLiveNow = liveIds.Contains(r.Id),
        }).ToList();
    }

    public async Task<BannerDetail?> GetAsync(
        long id,
        CancellationToken cancellationToken = default) =>
        await _db.Banners
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BannerDetail
            {
                Id = b.Id,
                Name = b.Name,
                Headline = b.Headline,
                Subheading = b.Subheading,
                ImagePath = b.ImagePath,
                MobileImagePath = b.MobileImagePath,
                AltText = b.AltText,
                LinkUrl = b.LinkUrl,
                ButtonText = b.ButtonText,
                DisplayOrder = b.DisplayOrder,
                IsActive = b.IsActive,
                StartsAtUtc = b.StartsAtUtc,
                EndsAtUtc = b.EndsAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Creates a banner. The image comes with it rather than afterwards,
    /// because a banner without one is not a banner - it would sit in the list
    /// looking finished and render as a broken box on the home page.
    /// </summary>
    public async Task<OperationResult<long>> CreateAsync(
        SaveBannerRequest request,
        Stream image,
        FileKind kind,
        Stream? mobileImage,
        FileKind? mobileKind,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);

        if (!validation.Succeeded)
        {
            return OperationResult<long>.Failure(validation.Error!, validation.Field);
        }

        if (await _db.Banners.CountAsync(cancellationToken) >= MaxBanners)
        {
            return OperationResult<long>.Failure(
                $"There are already {MaxBanners} banners. Delete an old one first.");
        }

        var storedPath = await _files.SaveAsync(image, "banners", kind, cancellationToken);

        string? storedMobilePath = null;

        if (mobileImage is not null && mobileKind is not null)
        {
            storedMobilePath = await _files.SaveAsync(
                mobileImage, "banners", mobileKind.Value, cancellationToken);
        }

        var banner = new Banner
        {
            Name = request.Name.Trim(),
            Headline = Trim(request.Headline),
            Subheading = Trim(request.Subheading),
            ImagePath = storedPath,
            MobileImagePath = storedMobilePath,
            AltText = request.AltText.Trim(),
            LinkUrl = Link(request.LinkUrl),
            ButtonText = Trim(request.ButtonText),
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
        };

        _db.Banners.Add(banner);
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(banner.Id);
    }

    /// <summary>
    /// Updates the words and the schedule. New images are optional: leaving
    /// both empty keeps the ones already there, so somebody fixing a typo in a
    /// headline does not have to find the artwork again.
    /// </summary>
    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveBannerRequest request,
        Stream? image,
        FileKind? kind,
        Stream? mobileImage,
        FileKind? mobileKind,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);

        if (!validation.Succeeded)
        {
            return validation;
        }

        var banner = await _db.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (banner is null)
        {
            return OperationResult.Failure("That banner no longer exists.");
        }

        // The old files are removed only after the new ones are safely written,
        // and only once the row has been saved. A delete first would leave the
        // home page pointing at nothing if the upload then failed.
        string? replacedDesktop = null;
        string? replacedMobile = null;

        if (image is not null && kind is not null)
        {
            replacedDesktop = banner.ImagePath;
            banner.ImagePath = await _files.SaveAsync(
                image, "banners", kind.Value, cancellationToken);
        }

        if (mobileImage is not null && mobileKind is not null)
        {
            replacedMobile = banner.MobileImagePath;
            banner.MobileImagePath = await _files.SaveAsync(
                mobileImage, "banners", mobileKind.Value, cancellationToken);
        }

        banner.Name = request.Name.Trim();
        banner.Headline = Trim(request.Headline);
        banner.Subheading = Trim(request.Subheading);
        banner.AltText = request.AltText.Trim();
        banner.LinkUrl = Link(request.LinkUrl);
        banner.ButtonText = Trim(request.ButtonText);
        banner.DisplayOrder = request.DisplayOrder;
        banner.IsActive = request.IsActive;
        banner.StartsAtUtc = request.StartsAtUtc;
        banner.EndsAtUtc = request.EndsAtUtc;

        await _db.SaveChangesAsync(cancellationToken);

        await DeleteFileAsync(replacedDesktop, cancellationToken);
        await DeleteFileAsync(replacedMobile, cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var banner = await _db.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (banner is null)
        {
            return OperationResult.Failure("That banner no longer exists.");
        }

        var desktop = banner.ImagePath;
        var mobile = banner.MobileImagePath;

        // A banner carries no history and appears on no document, so unlike a
        // product it really can be deleted rather than deactivated.
        _db.Banners.Remove(banner);
        await _db.SaveChangesAsync(cancellationToken);

        await DeleteFileAsync(desktop, cancellationToken);
        await DeleteFileAsync(mobile, cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> SetActiveAsync(
        long id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var banner = await _db.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (banner is null)
        {
            return OperationResult.Failure("That banner no longer exists.");
        }

        banner.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    // -----------------------------------------------------------------------

    private static OperationResult Validate(SaveBannerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return OperationResult.Failure(
                "Give the banner a name so you can find it later.",
                nameof(SaveBannerRequest.Name));
        }

        if (string.IsNullOrWhiteSpace(request.AltText))
        {
            return OperationResult.Failure(
                "Describe the image in a few words, for people who cannot see it.",
                nameof(SaveBannerRequest.AltText));
        }

        // A window that closes before it opens shows the banner never, silently.
        if (request.StartsAtUtc is { } from
            && request.EndsAtUtc is { } to
            && to <= from)
        {
            return OperationResult.Failure(
                "The end date has to be after the start date.",
                nameof(SaveBannerRequest.EndsAtUtc));
        }

        return OperationResult.Success();
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Either a path within this site ("/c/skincare") or an absolute http(s)
    /// URL. Anything else is dropped, because this value becomes an
    /// <c>href</c> on the home page and "javascript:" is not a destination.
    /// </summary>
    private static string? Link(string? value)
    {
        var trimmed = Trim(value);

        if (trimmed is null)
        {
            return null;
        }

        // A site-relative path. Rejecting "//evil.example" matters: browsers
        // read a leading double slash as a protocol-relative absolute URL, so
        // it looks internal and is not.
        if (trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? trimmed
            : null;
    }

    private async Task DeleteFileAsync(string? path, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            await _files.DeleteAsync(path, cancellationToken);
        }
    }
}
