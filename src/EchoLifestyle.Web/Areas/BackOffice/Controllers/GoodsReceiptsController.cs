using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Application.Purchasing.Suppliers;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// Receiving stock, and looking at what has been received.
///
/// Quick Purchase lives here rather than in its own controller because it
/// produces a goods receipt - the same document the formal purchase-order route
/// will produce. Keeping them together makes it harder for the two to drift
/// into different behaviour.
/// </summary>
[Authorize(Policy = Permissions.Procurement.GoodsReceiptCreate)]
public class GoodsReceiptsController : BackOfficeControllerBase
{
    private readonly GoodsReceiptService _receipts;
    private readonly SupplierAdminService _suppliers;
    private readonly ProductAdminService _products;
    private readonly ICurrentUser _currentUser;
    private readonly EchoDbContext _db;

    public GoodsReceiptsController(
        GoodsReceiptService receipts,
        SupplierAdminService suppliers,
        ProductAdminService products,
        ICurrentUser currentUser,
        EchoDbContext db)
    {
        _receipts = receipts;
        _suppliers = suppliers;
        _products = products;
        _currentUser = currentUser;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Suppliers"] = await _suppliers.GetOptionsAsync(cancellationToken: cancellationToken);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        long? supplierId,
        CancellationToken cancellationToken)
    {
        var page = await _receipts.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            supplierId,
            cancellationToken);

        return Json(GridResponse<GoodsReceiptListItem>.From(request, page));
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var detail = await _receipts.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Number;
        return View(detail);
    }

    // -----------------------------------------------------------------------
    // Quick Purchase
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Quick(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Quick purchase";

        var model = new QuickPurchaseFormModel();
        await PopulateAsync(model, cancellationToken);

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Quick(QuickPurchaseFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Quick purchase";

        if (model.Lines.Count(l => l.ProductVariantId > 0) == 0)
        {
            ModelState.AddModelError(
                string.Empty, "Add at least one product - a receipt with nothing in it moves no stock.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        var result = await _receipts.PostQuickPurchaseAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        // Straight to the posted document. Somebody who has just received a
        // delivery wants to see what it did, and the landed costs are only
        // knowable after posting.
        Notify("Stock received and posted.");
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    /// <summary>
    /// Variant search for the line picker. Returns at most a handful of rows -
    /// this is typed into, not browsed.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.ProductView)]
    public async Task<IActionResult> Lookup(string? term, CancellationToken cancellationToken)
    {
        var matches = await _products.LookupVariantsAsync(term, 15, cancellationToken);

        return Json(matches.Select(m => new
        {
            m.Id,
            m.Sku,
            m.Barcode,
            m.Display,
            m.BrandName,
            m.IsBatchTracked,
            m.IsExpiryTracked,
            m.LastUnitCost,
            m.CurrentPrice,
        }));
    }

    private async Task PopulateAsync(QuickPurchaseFormModel model, CancellationToken cancellationToken)
    {
        model.Suppliers = await _suppliers.GetOptionsAsync(
            model.SupplierId == 0 ? null : model.SupplierId, cancellationToken);

        // Branch scope comes from the signed-in user, not from the form. An
        // Owner sees every active branch; anyone else sees only theirs.
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
    }
}
