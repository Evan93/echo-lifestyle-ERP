using EchoLifestyle.Application.Catalog.Products;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Inventory.Adjustments;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Areas.BackOffice.Models.Inventory;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// Stock changing for a reason that is not a purchase or a sale.
///
/// Whether writing one posts it immediately or leaves it waiting depends on the
/// permission the user holds, not on which button they press. The screen says
/// which it will be before they commit.
/// </summary>
[Authorize(Policy = Permissions.Inventory.AdjustmentCreate)]
public class StockAdjustmentsController : BackOfficeControllerBase
{
    private readonly StockAdjustmentService _adjustments;
    private readonly GoodsReceiptService _receipts;
    private readonly ProductAdminService _products;
    private readonly ICurrentUser _currentUser;
    private readonly EchoDbContext _db;

    public StockAdjustmentsController(
        StockAdjustmentService adjustments,
        GoodsReceiptService receipts,
        ProductAdminService products,
        ICurrentUser currentUser,
        EchoDbContext db)
    {
        _adjustments = adjustments;
        _receipts = receipts;
        _products = products;
        _currentUser = currentUser;
        _db = db;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["CanApprove"] = _adjustments.CanApprove;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StockAdjustmentStatus? status,
        CancellationToken cancellationToken)
    {
        var page = await _adjustments.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            warehouseId: null,
            cancellationToken);

        return Json(GridResponse<StockAdjustmentListItem>.From(request, page));
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var detail = await _adjustments.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Number;
        ViewData["CanApprove"] = _adjustments.CanApprove;

        return View(detail);
    }

    // -----------------------------------------------------------------------
    // Writing one
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Adjust stock";

        var model = new StockAdjustmentFormModel();
        await PopulateAsync(model, cancellationToken);

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        StockAdjustmentFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Adjust stock";

        if (model.Lines.Count(l => l.ProductVariantId > 0 && l.StockBatchId > 0) == 0)
        {
            ModelState.AddModelError(
                string.Empty, "Add at least one line - an adjustment with nothing in it moves no stock.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        var result = await _adjustments.SubmitAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        var submission = result.Value!;

        Notify(submission.WasPosted
            ? "Adjustment posted. Stock has moved."
            : "Adjustment submitted. Nothing has moved yet - it is waiting for approval.");

        return RedirectToAction(nameof(Details), new { id = submission.Id });
    }

    /// <summary>
    /// The batches of one product that can be adjusted, for the second step of
    /// adding a line. Empty batches are included on purpose - stock coming back
    /// belongs to the batch it left from.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Batches(
        long productVariantId,
        long warehouseId,
        CancellationToken cancellationToken)
    {
        var batches = await _adjustments.GetBatchesAsync(
            productVariantId, warehouseId, cancellationToken);

        return Json(batches.Select(b => new
        {
            b.StockBatchId,
            b.Display,
            b.QuantityOnHand,
            b.IsAutoGenerated,
        }));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Catalog.ProductView)]
    public async Task<IActionResult> Lookup(string? term, CancellationToken cancellationToken)
    {
        var matches = await _products.LookupVariantsAsync(term, 15, cancellationToken);

        return Json(matches.Select(m => new { m.Id, m.Sku, m.Display, m.BrandName }));
    }

    // -----------------------------------------------------------------------
    // Deciding on one
    // -----------------------------------------------------------------------

    [HttpPost]
    [Authorize(Policy = Permissions.Inventory.AdjustmentApprove)]
    public async Task<IActionResult> Approve(
        long id,
        string? decisionNotes,
        CancellationToken cancellationToken)
    {
        var result = await _adjustments.ApproveAsync(id, decisionNotes, cancellationToken);

        Notify(
            result.Succeeded ? "Approved and posted. Stock has moved." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Inventory.AdjustmentApprove)]
    public async Task<IActionResult> Reject(
        RejectAdjustmentModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notify("Say why it is being turned down.", "danger");
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var result = await _adjustments.RejectAsync(model.Id, model.Reason, cancellationToken);

        Notify(
            result.Succeeded ? "Adjustment rejected. Nothing moved." : result.Error!,
            result.Succeeded ? "warning" : "danger");

        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    private async Task PopulateAsync(
        StockAdjustmentFormModel model,
        CancellationToken cancellationToken)
    {
        model.CanApprove = _adjustments.CanApprove;

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
