using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Areas.BackOffice.Models.Crm;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// Customers.
///
/// Contact details arrive from the service already masked when the signed-in
/// user lacks <c>Crm.Customer.ViewPii</c> - this controller never has the real
/// number to leak, which is the point of masking there rather than in a view.
/// </summary>
[Authorize(Policy = Permissions.Crm.CustomerView)]
public class CustomersController : BackOfficeControllerBase
{
    private readonly CustomerAdminService _customers;
    private readonly ICurrentUser _currentUser;
    private readonly EchoDbContext _db;

    public CustomersController(
        CustomerAdminService customers,
        ICurrentUser currentUser,
        EchoDbContext db)
    {
        _customers = customers;
        _currentUser = currentUser;
        _db = db;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["CanSeePii"] = _customers.CanSeePii;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StatusFilter status,
        bool blockedOnly,
        CancellationToken cancellationToken)
    {
        var page = await _customers.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            blockedOnly,
            cancellationToken);

        return Json(GridResponse<CustomerListItem>.From(request, page));
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        // recordAccess: this is a person deliberately opening one customer's
        // record, which is the read the audit trail is there to capture.
        var detail = await _customers.GetAsync(id, recordAccess: true, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.FullName;
        ViewData["CanSeePii"] = _customers.CanSeePii;
        ViewData["CanEdit"] = _currentUser.HasPermission(Permissions.Crm.CustomerEdit);
        ViewData["CanBlock"] = _currentUser.HasPermission(Permissions.Crm.CustomerBlock);
        ViewData["Divisions"] = await _customers.GetDivisionsAsync(cancellationToken);
        ViewData["Districts"] = await _customers.GetDistrictsAsync(cancellationToken);

        return View(detail);
    }

    // -----------------------------------------------------------------------
    // Creating and editing
    // -----------------------------------------------------------------------

    [HttpGet]
    [Authorize(Policy = Permissions.Crm.CustomerEdit)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New customer";

        var model = new CustomerFormModel();
        await PopulateAsync(model, cancellationToken);

        return View("Form", model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Crm.CustomerEdit)]
    public async Task<IActionResult> Create(
        CustomerFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New customer";

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _customers.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View("Form", model);
        }

        // Straight to the record, because the next thing anybody does with a
        // new customer is add the address to send their order to.
        Notify("Customer saved. Add a delivery address to be able to ship to them.");
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Crm.CustomerEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _customers.GetAsync(id, recordAccess: false, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        // Editing a masked record would save the mask over the real number.
        if (!_customers.CanSeePii)
        {
            Notify("You can view this customer but not edit their contact details.", "warning");
            return RedirectToAction(nameof(Details), new { id });
        }

        ViewData["Title"] = detail.FullName;

        var model = CustomerFormModel.From(detail);
        await PopulateAsync(model, cancellationToken);

        return View("Form", model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Crm.CustomerEdit)]
    public async Task<IActionResult> Edit(
        long id,
        CustomerFormModel model,
        CancellationToken cancellationToken)
    {
        model.Id = id;

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _customers.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View("Form", model);
        }

        Notify("Customer updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// A name and a number, from a dialog. Returns JSON because it is called
    /// from the middle of another screen - eventually the order screen.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Permissions.Crm.CustomerEdit)]
    public async Task<IActionResult> QuickCreate(
        QuickCreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _customers.QuickCreateAsync(request, cancellationToken);

        if (!result.Succeeded)
        {
            return Json(new { ok = false, error = result.Error, field = result.Field });
        }

        var created = result.Value!;

        return Json(new
        {
            ok = true,
            id = created.Id,
            code = created.Code,
            fullName = created.FullName,

            // The caller says something different for a customer we already
            // knew: silently reusing a record without saying so is how people
            // end up editing the wrong one.
            wasExisting = created.WasExisting,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Lookup(string? term, CancellationToken cancellationToken)
    {
        var matches = await _customers.LookupAsync(term, 10, cancellationToken);

        return Json(matches.Select(m => new
        {
            m.Id,
            m.Code,
            m.FullName,
            m.Phone,
            m.IsBlocked,
            m.BlockReason,
            m.DefaultAddress,
        }));
    }

    // -----------------------------------------------------------------------
    // Addresses
    // -----------------------------------------------------------------------

    [HttpPost]
    [Authorize(Policy = Permissions.Crm.CustomerEdit)]
    public async Task<IActionResult> SaveAddress(
        CustomerAddressFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notify(
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault()
                ?? "Check the address fields.",
                "danger");

            return RedirectToAction(nameof(Details), new { id = model.CustomerId });
        }

        var result = await _customers.SaveAddressAsync(model.ToRequest(), cancellationToken);

        Notify(
            result.Succeeded ? "Address saved." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id = model.CustomerId });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Crm.CustomerEdit)]
    public async Task<IActionResult> DeleteAddress(
        long customerId,
        long addressId,
        CancellationToken cancellationToken)
    {
        var result = await _customers.DeleteAddressAsync(customerId, addressId, cancellationToken);

        Notify(
            result.Succeeded ? "Address removed." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id = customerId });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Crm.CustomerEdit)]
    public async Task<IActionResult> SetDefaultAddress(
        long customerId,
        long addressId,
        CancellationToken cancellationToken)
    {
        var result = await _customers.SetDefaultAddressAsync(customerId, addressId, cancellationToken);

        Notify(
            result.Succeeded ? "Default address changed." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id = customerId });
    }

    // -----------------------------------------------------------------------
    // Blocking
    // -----------------------------------------------------------------------

    [HttpPost]
    [Authorize(Policy = Permissions.Crm.CustomerBlock)]
    public async Task<IActionResult> Block(
        BlockCustomerModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notify("Say why they are being blocked.", "danger");
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await _customers.BlockAsync(model.Id, model.Reason, cancellationToken);

        Notify(
            result.Succeeded ? "Blocked. New orders will warn before they are taken." : result.Error!,
            result.Succeeded ? "warning" : "danger");

        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Crm.CustomerBlock)]
    public async Task<IActionResult> Unblock(
        long id,
        string? note,
        CancellationToken cancellationToken)
    {
        var result = await _customers.UnblockAsync(id, note, cancellationToken);

        Notify(
            result.Succeeded ? "Unblocked." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateAsync(CustomerFormModel model, CancellationToken cancellationToken)
    {
        model.PriceLists = await _db.PriceLists
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new PriceListOption { Id = p.Id, Name = p.Name })
            .ToListAsync(cancellationToken);
    }
}
