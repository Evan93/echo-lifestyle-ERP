using EchoLifestyle.Application.Administration.Branches;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Web.Areas.BackOffice.Models.Branches;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Authorize(Policy = Permissions.Administration.BranchView)]
public class BranchesController : BackOfficeControllerBase
{
    private readonly BranchAdminService _branches;

    public BranchesController(BranchAdminService branches)
    {
        _branches = branches;
    }

    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>Grid data. Paging, searching and sorting all happen in SQL.</summary>
    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StatusFilter status,
        CancellationToken cancellationToken)
    {
        var page = await _branches.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            cancellationToken);

        return Json(GridResponse<BranchListItem>.From(request, page));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Administration.BranchEdit)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var warehouses = await _branches.GetLinkableWarehousesAsync(cancellationToken);

        ViewData["Title"] = "New branch";
        return View("Form", BranchFormModel.ForCreate(warehouses));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Administration.BranchEdit)]
    public async Task<IActionResult> Create(BranchFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New branch";

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var result = await _branches.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            // Business failures land on the field they belong to, so the form
            // highlights the actual problem rather than showing a banner.
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("Form", model);
        }

        Notify($"Branch {model.Code.ToUpperInvariant()} created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Administration.BranchEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _branches.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Branch {detail.Code}";
        return View("Form", BranchFormModel.FromDetail(detail));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Administration.BranchEdit)]
    public async Task<IActionResult> Edit(long id, BranchFormModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        ViewData["Title"] = $"Branch {model.Code}";

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var result = await _branches.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("Form", model);
        }

        // Say where it went. Without this, an inactive branch simply vanishes
        // from the default list and the save looks like it broke something.
        Notify(model.IsActive
            ? $"Branch {model.Code.ToUpperInvariant()} saved."
            : $"Branch {model.Code.ToUpperInvariant()} saved and set inactive - it is hidden by the Active filter.");

        return RedirectToAction(nameof(Index));
    }
}
