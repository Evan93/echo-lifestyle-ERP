using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Authorize(Policy = Permissions.Procurement.SupplierView)]
public class SuppliersController : BackOfficeControllerBase
{
    private readonly SupplierAdminService _suppliers;

    public SuppliersController(SupplierAdminService suppliers)
    {
        _suppliers = suppliers;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StatusFilter status,
        CancellationToken cancellationToken)
    {
        var page = await _suppliers.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            cancellationToken);

        return Json(GridResponse<SupplierListItem>.From(request, page));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Procurement.SupplierEdit)]
    public IActionResult Create()
    {
        ViewData["Title"] = "New supplier";
        return View("Form", new SupplierFormModel());
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Procurement.SupplierEdit)]
    public async Task<IActionResult> Create(SupplierFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New supplier";

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var result = await _suppliers.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("Form", model);
        }

        Notify($"Supplier '{model.Name}' created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Procurement.SupplierEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _suppliers.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Name;
        return View("Form", SupplierFormModel.FromDetail(detail));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Procurement.SupplierEdit)]
    public async Task<IActionResult> Edit(long id, SupplierFormModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        ViewData["Title"] = model.Name;

        if (!ModelState.IsValid)
        {
            await RestoreReadOnlyAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _suppliers.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await RestoreReadOnlyAsync(model, cancellationToken);
            return View("Form", model);
        }

        Notify(model.IsActive
            ? $"Supplier '{model.Name}' saved."
            : $"Supplier '{model.Name}' saved and set inactive - they will not appear on purchase screens.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Procurement.SupplierEdit)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var result = await _suppliers.DeleteAsync(id, cancellationToken);

        if (!result.Succeeded)
        {
            Notify(result.Error!, "danger");
            return RedirectToAction(nameof(Edit), new { id });
        }

        Notify("Supplier deleted.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// The batch count is not posted by the form - it decides whether Delete is
    /// offered, so it comes from the database rather than from the browser.
    /// </summary>
    private async Task RestoreReadOnlyAsync(SupplierFormModel model, CancellationToken cancellationToken)
    {
        var detail = await _suppliers.GetAsync(model.Id, cancellationToken);

        if (detail is not null)
        {
            model.BatchCount = detail.BatchCount;
        }
    }
}
