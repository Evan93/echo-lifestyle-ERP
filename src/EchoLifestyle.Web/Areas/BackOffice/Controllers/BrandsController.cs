using EchoLifestyle.Application.Catalog.Brands;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Web.Areas.BackOffice.Models.Catalog;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Authorize(Policy = Permissions.Catalog.BrandView)]
public class BrandsController : BackOfficeControllerBase
{
    private readonly BrandAdminService _brands;

    public BrandsController(BrandAdminService brands)
    {
        _brands = brands;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StatusFilter status,
        CancellationToken cancellationToken)
    {
        var page = await _brands.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            cancellationToken);

        return Json(GridResponse<BrandListItem>.From(request, page));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.BrandEdit)]
    public IActionResult Create()
    {
        ViewData["Title"] = "New brand";
        return View("Form", new BrandFormModel());
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.BrandEdit)]
    public async Task<IActionResult> Create(BrandFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New brand";

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var result = await _brands.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("Form", model);
        }

        Notify($"Brand '{model.Name}' created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.BrandEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _brands.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Name;
        return View("Form", BrandFormModel.FromDetail(detail));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.BrandEdit)]
    public async Task<IActionResult> Edit(long id, BrandFormModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        ViewData["Title"] = model.Name;

        if (!ModelState.IsValid)
        {
            await RestoreReadOnlyAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _brands.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await RestoreReadOnlyAsync(model, cancellationToken);
            return View("Form", model);
        }

        Notify(model.IsActive
            ? $"Brand '{model.Name}' saved."
            : $"Brand '{model.Name}' saved and set inactive - it is hidden from the storefront and the product form.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Catalog.BrandEdit)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var result = await _brands.DeleteAsync(id, cancellationToken);

        if (!result.Succeeded)
        {
            Notify(result.Error!, "danger");
            return RedirectToAction(nameof(Edit), new { id });
        }

        Notify("Brand deleted.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The product count is not posted by the form - it decides whether Delete
    /// is offered, so it has to come from the database rather than from the
    /// browser.
    /// </summary>
    private async Task RestoreReadOnlyAsync(BrandFormModel model, CancellationToken cancellationToken)
    {
        var detail = await _brands.GetAsync(model.Id, cancellationToken);

        if (detail is not null)
        {
            model.ProductCount = detail.ProductCount;
        }
    }
}
