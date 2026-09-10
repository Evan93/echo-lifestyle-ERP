using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Application.Marketing;
using EchoLifestyle.Web.Areas.BackOffice.Models.Marketing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// The home page banner.
///
/// Its own permission rather than reuse of the catalogue's: deciding what the
/// shop advertises and deciding what a product costs are different jobs, and
/// the first is one you might reasonably hand to somebody who should not be
/// touching prices.
/// </summary>
[Authorize(Policy = Permissions.Marketing.BannerView)]
public class BannersController : BackOfficeControllerBase
{
    /// <summary>
    /// Generous for a photograph straight off a phone, and low enough that a
    /// twelve-megabyte banner cannot be uploaded and then served to every
    /// visitor on a mobile connection.
    /// </summary>
    private const int MaxUploadBytes = 8 * 1024 * 1024;

    private readonly BannerAdminService _banners;

    public BannersController(BannerAdminService banners)
    {
        _banners = banners;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Home page banner";
        ViewData["CanEdit"] = User.HasClaim(
            Permissions.ClaimType, Permissions.Marketing.BannerEdit);

        return View(await _banners.ListAsync(cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Marketing.BannerEdit)]
    public IActionResult Create()
    {
        ViewData["Title"] = "New banner";

        return View("Form", new BannerFormModel());
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Marketing.BannerEdit)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Create(
        BannerFormModel model,
        IFormFile? image,
        IFormFile? mobileImage,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New banner";

        if (image is null || image.Length == 0)
        {
            ModelState.AddModelError(nameof(image), "Choose the banner image.");
        }

        var kind = Check(image, nameof(image));
        var mobileKind = Check(mobileImage, nameof(mobileImage));

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        await using var content = image!.OpenReadStream();
        await using var mobileContent = mobileImage?.OpenReadStream() ?? Stream.Null;

        var result = await _banners.CreateAsync(
            model.ToRequest(),
            content,
            kind!.Value,
            mobileImage is null ? null : mobileContent,
            mobileKind,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("Form", model);
        }

        Notify("Banner saved.");

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Marketing.BannerEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _banners.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Name;

        return View("Form", BannerFormModel.FromDetail(detail));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Marketing.BannerEdit)]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Edit(
        long id,
        BannerFormModel model,
        IFormFile? image,
        IFormFile? mobileImage,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = model.Name;

        // Both images are optional here: somebody fixing a typo in a headline
        // should not have to find the artwork again.
        var kind = Check(image, nameof(image));
        var mobileKind = Check(mobileImage, nameof(mobileImage));

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        await using var content = image?.OpenReadStream() ?? Stream.Null;
        await using var mobileContent = mobileImage?.OpenReadStream() ?? Stream.Null;

        var result = await _banners.UpdateAsync(
            id,
            model.ToRequest(),
            image is null ? null : content,
            kind,
            mobileImage is null ? null : mobileContent,
            mobileKind,
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("Form", model);
        }

        Notify("Banner saved.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Marketing.BannerEdit)]
    public async Task<IActionResult> Toggle(
        long id,
        bool active,
        CancellationToken cancellationToken)
    {
        var result = await _banners.SetActiveAsync(id, active, cancellationToken);

        Notify(
            result.Succeeded
                ? (active ? "Banner switched on." : "Banner switched off.")
                : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Marketing.BannerEdit)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var result = await _banners.DeleteAsync(id, cancellationToken);

        Notify(result.Succeeded ? "Banner deleted." : result.Error!,
               result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Maps the declared content type onto the closed set of kinds the file
    /// store accepts, and records a model error rather than throwing.
    ///
    /// The declared type is a claim, not proof - the store verifies the bytes
    /// before it writes anything - so this is a courtesy check that gives a
    /// useful message, not the thing keeping a disguised file out.
    /// </summary>
    private FileKind? Check(IFormFile? file, string field)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        if (file.Length > MaxUploadBytes)
        {
            ModelState.AddModelError(
                field, $"{file.FileName} is too large. Keep banners under 8 MB.");

            return null;
        }

        var kind = file.ContentType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" or "image/pjpeg" => FileKind.Jpeg,
            "image/png" => FileKind.Png,
            "image/webp" => FileKind.WebP,
            _ => (FileKind?)null,
        };

        if (kind is null)
        {
            ModelState.AddModelError(
                field, $"{file.FileName}: only JPEG, PNG and WebP images are accepted.");
        }

        return kind;
    }
}
