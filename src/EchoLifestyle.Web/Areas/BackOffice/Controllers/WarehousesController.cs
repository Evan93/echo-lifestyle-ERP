using EchoLifestyle.Application.Administration.Warehouses;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Web.Areas.BackOffice.Models.Warehouses;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Authorize(Policy = Permissions.Administration.WarehouseView)]
public class WarehousesController : BackOfficeControllerBase
{
    private readonly WarehouseAdminService _warehouses;

    public WarehousesController(WarehouseAdminService warehouses)
    {
        _warehouses = warehouses;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StatusFilter status,
        CancellationToken cancellationToken)
    {
        var page = await _warehouses.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            cancellationToken);

        return Json(GridResponse<WarehouseListItem>.From(request, page));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Administration.WarehouseEdit)]
    public IActionResult Create()
    {
        ViewData["Title"] = "New warehouse";
        return View("Form", new WarehouseFormModel());
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Administration.WarehouseEdit)]
    public async Task<IActionResult> Create(WarehouseFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New warehouse";

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var result = await _warehouses.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("Form", model);
        }

        Notify($"Warehouse {model.Code.ToUpperInvariant()} created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Administration.WarehouseEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _warehouses.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Warehouse {detail.Code}";
        return View("Form", WarehouseFormModel.FromDetail(detail));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Administration.WarehouseEdit)]
    public async Task<IActionResult> Edit(long id, WarehouseFormModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        ViewData["Title"] = $"Warehouse {model.Code}";

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var result = await _warehouses.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);

            // Re-read the read-only link lists so the form still shows them
            // after a failed save.
            var detail = await _warehouses.GetAsync(id, cancellationToken);
            if (detail is not null)
            {
                model.LinkedBranches = detail.LinkedBranches;
                model.PrimaryForBranches = detail.PrimaryForBranches;
            }

            return View("Form", model);
        }

        Notify(model.IsActive
            ? $"Warehouse {model.Code.ToUpperInvariant()} saved."
            : $"Warehouse {model.Code.ToUpperInvariant()} saved and set inactive - it is hidden by the Active filter.");

        return RedirectToAction(nameof(Index));
    }
}
