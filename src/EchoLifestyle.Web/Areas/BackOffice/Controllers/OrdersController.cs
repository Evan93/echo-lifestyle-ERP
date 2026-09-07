using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;
using EchoLifestyle.Web.Areas.BackOffice.Models.Sales;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// Orders, from the message that started one to the money that came back.
///
/// The buttons on an order are gated separately from writing one: taking an
/// order and handing a parcel to a courier are different jobs, and in a
/// cash-on-delivery business the second is where the money is.
/// </summary>
[Authorize(Policy = Permissions.Sales.OrderView)]
public class OrdersController : BackOfficeControllerBase
{
    private readonly SalesOrderService _orders;
    private readonly CustomerAdminService _customers;
    private readonly GoodsReceiptService _receipts;
    private readonly ICurrentUser _currentUser;
    private readonly EchoDbContext _db;

    public OrdersController(
        SalesOrderService orders,
        CustomerAdminService customers,
        GoodsReceiptService receipts,
        ICurrentUser currentUser,
        EchoDbContext db)
    {
        _orders = orders;
        _customers = customers;
        _receipts = receipts;
        _currentUser = currentUser;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Workload"] = await _orders.GetWorkloadAsync(cancellationToken);
        ViewData["CanCreate"] = _currentUser.HasPermission(Permissions.Sales.OrderCreate);

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        SalesOrderStatus? status,
        bool awaitingCashOnly,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var page = await _orders.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            awaitingCashOnly,
            from,
            to,
            cancellationToken);

        return Json(GridResponse<SalesOrderListItem>.From(request, page));
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var detail = await _orders.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Number;
        SetActionPermissions();

        return View(detail);
    }

    // -----------------------------------------------------------------------
    // Taking one
    // -----------------------------------------------------------------------

    [HttpGet]
    [Authorize(Policy = Permissions.Sales.OrderCreate)]
    public async Task<IActionResult> Create(long? customerId, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New order";

        var model = new OrderFormModel { CustomerId = customerId ?? 0 };
        await PopulateAsync(model, cancellationToken);

        return View("Form", model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderCreate)]
    public async Task<IActionResult> Create(OrderFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New order";

        if (model.Lines.Count(l => l.ProductVariantId > 0) == 0)
        {
            ModelState.AddModelError(
                string.Empty, "Add at least one product - an order with nothing in it sells nothing.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _orders.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View("Form", model);
        }

        Notify("Order saved as a draft. Nothing is reserved until you confirm it.");
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Sales.OrderCreate)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _orders.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        if (!SalesOrder.IsEditable(detail.Status))
        {
            Notify("Only a draft can be edited. This one has already been confirmed.", "warning");
            return RedirectToAction(nameof(Details), new { id });
        }

        ViewData["Title"] = detail.Number;

        var model = OrderFormModel.From(detail);
        await PopulateAsync(model, cancellationToken);

        return View("Form", model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderCreate)]
    public async Task<IActionResult> Edit(
        long id,
        OrderFormModel model,
        CancellationToken cancellationToken)
    {
        model.Id = id;

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _orders.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View("Form", model);
        }

        Notify("Order updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderCreate)]
    public async Task<IActionResult> DeleteDraft(long id, CancellationToken cancellationToken)
    {
        var result = await _orders.DeleteDraftAsync(id, cancellationToken);

        if (!result.Succeeded)
        {
            Notify(result.Error!, "danger");
            return RedirectToAction(nameof(Details), new { id });
        }

        Notify("Draft deleted.");
        return RedirectToAction(nameof(Index));
    }

    // -----------------------------------------------------------------------
    // Moving one along
    // -----------------------------------------------------------------------

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderConfirm)]
    public async Task<IActionResult> Confirm(long id, string? note, CancellationToken cancellationToken)
    {
        var result = await _orders.ConfirmAsync(id, note, cancellationToken);

        Notify(
            result.Succeeded
                ? "Confirmed. Stock is reserved - still on the shelf, no longer sellable to anyone else."
                : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderDispatch)]
    public async Task<IActionResult> Pack(long id, string? note, CancellationToken cancellationToken)
    {
        var result = await _orders.MarkPackedAsync(id, note, cancellationToken);

        Notify(
            result.Succeeded ? "Marked packed." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderDispatch)]
    public async Task<IActionResult> Dispatch(
        DispatchFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notify("Name the courier before dispatching.", "danger");
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await _orders.DispatchAsync(
            new DispatchRequest
            {
                OrderId = model.Id,
                CourierName = model.CourierName,
                ConsignmentNumber = model.ConsignmentNumber,
                Note = model.Note,
            },
            cancellationToken);

        Notify(
            result.Succeeded ? "Dispatched. Stock has left and the ledger says so." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderDispatch)]
    public async Task<IActionResult> Deliver(
        DeliveryFormModel model,
        CancellationToken cancellationToken)
    {
        var result = await _orders.MarkDeliveredAsync(
            new DeliveryRequest
            {
                OrderId = model.Id,
                AmountCollected = model.AmountCollected,
                Note = model.Note,
            },
            cancellationToken);

        Notify(
            result.Succeeded ? "Marked delivered." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderDispatch)]
    public async Task<IActionResult> Return(
        OrderReasonModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notify("Say why the parcel came back.", "danger");
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await _orders.MarkReturnedAsync(model.Id, model.Reason, cancellationToken);

        Notify(
            result.Succeeded ? "Recorded as returned. Stock is back on the shelf." : result.Error!,
            result.Succeeded ? "warning" : "danger");

        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Sales.OrderCancel)]
    public async Task<IActionResult> Cancel(
        OrderReasonModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notify("Say why it was cancelled.", "danger");
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await _orders.CancelAsync(model.Id, model.Reason, cancellationToken);

        Notify(
            result.Succeeded ? "Cancelled. Anything reserved is back on the shelf." : result.Error!,
            result.Succeeded ? "warning" : "danger");

        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    // -----------------------------------------------------------------------
    // Pickers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Products for the line picker: what they cost this customer and how many
    /// can actually be promised.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Sales.OrderCreate)]
    public async Task<IActionResult> Lookup(
        string? term,
        long? customerId,
        long warehouseId,
        CancellationToken cancellationToken)
    {
        var matches = await _orders.SearchSellableAsync(
            term, customerId, warehouseId, 15, cancellationToken);

        return Json(matches.Select(m => new
        {
            m.ProductVariantId,
            m.Sku,
            m.Display,
            m.BrandName,
            m.UnitPrice,
            m.Available,
        }));
    }

    /// <summary>The chosen customer's addresses, for the delivery picker.</summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Sales.OrderCreate)]
    public async Task<IActionResult> Addresses(long customerId, CancellationToken cancellationToken)
    {
        var customer = await _customers.GetAsync(customerId, false, cancellationToken);

        if (customer is null)
        {
            return Json(Array.Empty<object>());
        }

        return Json(customer.Addresses.Select(a => new
        {
            a.Id,
            a.Label,
            a.IsDefault,
            a.IsInsideCity,
            a.DistrictName,
            OneLine = a.OneLine,
            Recipient = a.RecipientName,
        }));
    }

    private void SetActionPermissions()
    {
        ViewData["CanCreate"] = _currentUser.HasPermission(Permissions.Sales.OrderCreate);
        ViewData["CanConfirm"] = _currentUser.HasPermission(Permissions.Sales.OrderConfirm);
        ViewData["CanDispatch"] = _currentUser.HasPermission(Permissions.Sales.OrderDispatch);
        ViewData["CanCancel"] = _currentUser.HasPermission(Permissions.Sales.OrderCancel);
        ViewData["ShowCost"] = _currentUser.HasPermission(Permissions.Catalog.CostPriceView);
    }

    private async Task PopulateAsync(OrderFormModel model, CancellationToken cancellationToken)
    {
        var branchQuery = _db.Branches.AsNoTracking().Where(b => b.IsActive);

        if (!_currentUser.IsOwner)
        {
            var branchIds = _currentUser.BranchIds;
            branchQuery = branchQuery.Where(b => branchIds.Contains(b.Id));
        }

        model.Branches = await branchQuery
            .OrderBy(b => b.Code)
            .Select(b => new BranchOption { Id = b.Id, Code = b.Code, Name = b.Name })
            .ToListAsync(cancellationToken);

        if (model.BranchId == 0)
        {
            model.BranchId = model.Branches.FirstOrDefault()?.Id ?? 0;
        }

        model.Warehouses = await _receipts.GetWarehouseOptionsAsync(model.BranchId, cancellationToken);

        if (model.WarehouseId == 0)
        {
            model.WarehouseId =
                model.Warehouses.FirstOrDefault(w => w.IsPrimary)?.Id
                ?? model.Warehouses.FirstOrDefault()?.Id
                ?? 0;
        }

        if (model.CustomerId > 0)
        {
            var customer = await _customers.GetAsync(model.CustomerId, false, cancellationToken);

            if (customer is not null)
            {
                model.CustomerName = customer.FullName;
                model.Addresses = customer.Addresses;
            }
        }
    }
}
