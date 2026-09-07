using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Inventory;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// Looking at stock. Nothing here changes it - every screen is a read of the
/// balance projection, and the one write (a rebuild) only ever makes the
/// projection agree with the ledger again.
/// </summary>
[Authorize(Policy = Permissions.Inventory.StockView)]
public class StockController : BackOfficeControllerBase
{
    private readonly StockQueryService _stock;
    private readonly StockBalanceRebuildService _rebuild;
    private readonly EchoDbContext _db;

    public StockController(
        StockQueryService stock,
        StockBalanceRebuildService rebuild,
        EchoDbContext db)
    {
        _stock = stock;
        _rebuild = rebuild;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Warehouses"] = await WarehousesAsync(cancellationToken);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        long? warehouseId,
        bool includeZero,
        CancellationToken cancellationToken)
    {
        var page = await _stock.ListOnHandAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            warehouseId,
            includeZero,
            cancellationToken);

        return Json(GridResponse<StockOnHandItem>.From(request, page));
    }

    /// <summary>
    /// The batches behind one row, expanded in place. Oldest expiry first,
    /// because that is the order the stock should leave in.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Batches(
        long productVariantId,
        long? warehouseId,
        CancellationToken cancellationToken)
    {
        var batches = await _stock.GetBatchesAsync(productVariantId, warehouseId, cancellationToken);

        return PartialView("_Batches", batches);
    }

    // -----------------------------------------------------------------------
    // Movements
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Ledger(CancellationToken cancellationToken)
    {
        ViewData["Warehouses"] = await WarehousesAsync(cancellationToken);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> LedgerData(
        GridRequest request,
        long? productVariantId,
        long? warehouseId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var page = await _stock.ListMovementsAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            productVariantId,
            warehouseId,
            from,
            to,
            cancellationToken);

        return Json(GridResponse<StockMovementItem>.From(request, page));
    }

    // -----------------------------------------------------------------------
    // Expiry
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Expiring(
        int days = 90,
        long? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["Warehouses"] = await WarehousesAsync(cancellationToken);
        ViewData["Days"] = days;
        ViewData["WarehouseId"] = warehouseId;

        var rows = await _stock.ListExpiringAsync(days, warehouseId, cancellationToken);

        return View(rows);
    }

    // -----------------------------------------------------------------------
    // Rebuild
    // -----------------------------------------------------------------------

    [HttpPost]
    [Authorize(Policy = Permissions.Inventory.BalanceRebuild)]
    public async Task<IActionResult> Rebuild(CancellationToken cancellationToken)
    {
        var result = await _rebuild.RebuildAsync(cancellationToken);

        if (!result.Succeeded)
        {
            Notify(result.Error!, "danger");
            return RedirectToAction(nameof(Index));
        }

        var summary = result.Value!;

        // A rebuild that changed something is worth a louder colour than one
        // that confirmed everything already agreed.
        Notify(summary.Describe(), summary.FoundNothingWrong ? "success" : "warning");

        return RedirectToAction(nameof(Index));
    }

    private Task<List<WarehouseFilterOption>> WarehousesAsync(CancellationToken cancellationToken) =>
        _db.Warehouses
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseFilterOption { Id = w.Id, Name = w.Name })
            .ToListAsync(cancellationToken);
}

public class WarehouseFilterOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
