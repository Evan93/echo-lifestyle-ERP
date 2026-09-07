using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Inventory.Counts;
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
/// Counting the shelf.
///
/// Three screens for three steps: start a sheet, type in what was found, review
/// and post. Posting needs a permission the counting screens do not, because
/// counting is clerical and accepting a loss is not.
/// </summary>
[Authorize(Policy = Permissions.Inventory.CountCreate)]
public class StockCountsController : BackOfficeControllerBase
{
    private readonly StockCountService _counts;
    private readonly GoodsReceiptService _receipts;
    private readonly ICurrentUser _currentUser;
    private readonly EchoDbContext _db;

    public StockCountsController(
        StockCountService counts,
        GoodsReceiptService receipts,
        ICurrentUser currentUser,
        EchoDbContext db)
    {
        _counts = counts;
        _receipts = receipts;
        _currentUser = currentUser;
        _db = db;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StockCountStatus? status,
        CancellationToken cancellationToken)
    {
        var page = await _counts.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            cancellationToken);

        return Json(GridResponse<StockCountListItem>.From(request, page));
    }

    // -----------------------------------------------------------------------
    // Starting one
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Start a stock count";

        var model = new StartCountFormModel();
        await PopulateAsync(model, cancellationToken);

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Start(
        StartCountFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Start a stock count";

        if (model.Scope != StockCountScope.Everything && model.ScopeId is null)
        {
            ModelState.AddModelError(
                nameof(model.ScopeId), $"Choose which {model.Scope.ToString().ToLowerInvariant()} to count.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        var result = await _counts.StartAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        Notify("Count sheet ready. Nothing moves until you post it.");
        return RedirectToAction(nameof(Sheet), new { id = result.Value });
    }

    // -----------------------------------------------------------------------
    // Counting
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Sheet(long id, CancellationToken cancellationToken)
    {
        var detail = await _counts.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        // A posted or cancelled count has nothing left to type into; it goes
        // straight to the record of what happened.
        if (detail.Status != StockCountStatus.Counting)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        ViewData["Title"] = detail.Number;
        ViewData["CanPost"] = CanPost;

        return View(detail);
    }

    [HttpPost]
    public async Task<IActionResult> Sheet(
        CountSheetFormModel model,
        string? action,
        CancellationToken cancellationToken)
    {
        var saved = await _counts.SaveCountsAsync(model.Id, model.ToEntries(), cancellationToken);

        if (!saved.Succeeded)
        {
            Notify(saved.Error!, "danger");
            return RedirectToAction(nameof(Sheet), new { id = model.Id });
        }

        // Saving and reviewing are the same post. Somebody counting a long
        // shelf saves several times before they are ready to look at the
        // variances.
        if (string.Equals(action, "review", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Review), new { id = model.Id });
        }

        Notify("Counts saved. Nothing has moved yet.");
        return RedirectToAction(nameof(Sheet), new { id = model.Id });
    }

    // -----------------------------------------------------------------------
    // Reviewing and posting
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Review(long id, CancellationToken cancellationToken)
    {
        var detail = await _counts.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        if (detail.Status != StockCountStatus.Counting)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        ViewData["Title"] = $"{detail.Number} · review";
        ViewData["CanPost"] = CanPost;

        return View(detail);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Inventory.CountPost)]
    public async Task<IActionResult> Post(long id, CancellationToken cancellationToken)
    {
        var result = await _counts.PostAsync(id, cancellationToken);

        if (!result.Succeeded)
        {
            Notify(result.Error!, "danger");
            return RedirectToAction(nameof(Review), new { id });
        }

        var summary = result.Value!;

        Notify(summary.Describe(), summary.LinesPosted == 0 ? "success" : "warning");

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(long id, string? reason, CancellationToken cancellationToken)
    {
        var result = await _counts.CancelAsync(id, reason, cancellationToken);

        Notify(
            result.Succeeded ? "Count cancelled. Nothing moved." : result.Error!,
            result.Succeeded ? "warning" : "danger");

        return RedirectToAction(result.Succeeded ? nameof(Details) : nameof(Sheet), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var detail = await _counts.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Number;

        return View(detail);
    }

    private bool CanPost =>
        _currentUser.IsOwner || _currentUser.HasPermission(Permissions.Inventory.CountPost);

    private async Task PopulateAsync(StartCountFormModel model, CancellationToken cancellationToken)
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

        model.Brands = await _counts.GetBrandOptionsAsync(cancellationToken);
        model.Categories = await _counts.GetCategoryOptionsAsync(cancellationToken);
    }
}
