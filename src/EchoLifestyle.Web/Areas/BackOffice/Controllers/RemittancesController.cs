using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Finance.Remittances;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Areas.BackOffice.Models.Finance;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// Courier payouts.
///
/// One transfer covering forty parcels, minus their fee, against a statement of
/// consignment numbers. This screen is where that becomes forty settled orders,
/// a recorded cost of delivery, and whatever came back put on the shelf.
/// </summary>
[Authorize(Policy = Permissions.Finance.CashView)]
public class RemittancesController : BackOfficeControllerBase
{
    private readonly CourierRemittanceService _remittances;
    private readonly ICurrentUser _currentUser;
    private readonly EchoDbContext _db;

    public RemittancesController(
        CourierRemittanceService remittances,
        ICurrentUser currentUser,
        EchoDbContext db)
    {
        _remittances = remittances;
        _currentUser = currentUser;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // What every courier is holding, before anything else. It is the
        // question the screen exists to answer.
        ViewData["Exposure"] = await _remittances.GetExposureAsync(cancellationToken);
        ViewData["CanPost"] = _currentUser.HasPermission(Permissions.Finance.RemittancePost);

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        CourierRemittanceStatus? status,
        CancellationToken cancellationToken)
    {
        var page = await _remittances.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            cancellationToken);

        return Json(GridResponse<RemittanceListItem>.From(request, page));
    }

    // -----------------------------------------------------------------------
    // Starting one
    // -----------------------------------------------------------------------

    [HttpGet]
    [Authorize(Policy = Permissions.Finance.RemittancePost)]
    public async Task<IActionResult> Start(string? courier, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Record a courier payout";

        var model = new StartRemittanceFormModel { CourierName = courier ?? string.Empty };
        await PopulateAsync(model, cancellationToken);

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Finance.RemittancePost)]
    public async Task<IActionResult> Start(
        StartRemittanceFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Record a courier payout";

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        var result = await _remittances.StartAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        Notify("Started. Tick off the orders on the statement, then post it.");
        return RedirectToAction(nameof(Reconcile), new { id = result.Value });
    }

    // -----------------------------------------------------------------------
    // Reconciling
    // -----------------------------------------------------------------------

    [HttpGet]
    [Authorize(Policy = Permissions.Finance.RemittancePost)]
    public async Task<IActionResult> Reconcile(long id, CancellationToken cancellationToken)
    {
        var detail = await _remittances.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        if (detail.Status != CourierRemittanceStatus.Draft)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        ViewData["Title"] = detail.Number;

        // Everything this courier is still holding, so the statement can be
        // ticked off against it rather than typed from scratch.
        ViewData["Unsettled"] = await _remittances.GetUnsettledAsync(
            detail.CourierName, cancellationToken);

        return View(detail);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Finance.RemittancePost)]
    public async Task<IActionResult> Reconcile(
        ReconcileFormModel model,
        string? action,
        CancellationToken cancellationToken)
    {
        var saved = await _remittances.SaveAsync(model.ToRequest(), cancellationToken);

        if (!saved.Succeeded)
        {
            Notify(saved.Error!, "danger");
            return RedirectToAction(nameof(Reconcile), new { id = model.Id });
        }

        if (!string.Equals(action, "post", StringComparison.OrdinalIgnoreCase))
        {
            Notify("Saved. Nothing has been recorded yet.");
            return RedirectToAction(nameof(Reconcile), new { id = model.Id });
        }

        var posted = await _remittances.PostAsync(
            model.Id, model.AcceptDiscrepancy, cancellationToken);

        if (!posted.Succeeded)
        {
            Notify(posted.Error!, "danger");
            return RedirectToAction(nameof(Reconcile), new { id = model.Id });
        }

        Notify(posted.Value!.Describe());
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Finance.RemittancePost)]
    public async Task<IActionResult> Cancel(
        long id,
        string? reason,
        CancellationToken cancellationToken)
    {
        var result = await _remittances.CancelAsync(id, reason, cancellationToken);

        Notify(
            result.Succeeded ? "Cancelled. Nothing was recorded." : result.Error!,
            result.Succeeded ? "warning" : "danger");

        return RedirectToAction(result.Succeeded ? nameof(Index) : nameof(Reconcile), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var detail = await _remittances.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Number;

        return View(detail);
    }

    private async Task PopulateAsync(
        StartRemittanceFormModel model,
        CancellationToken cancellationToken)
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

        // Couriers actually used, so the box offers what this business ships
        // with rather than a hard-coded list.
        model.KnownCouriers = await _db.SalesOrders
            .AsNoTracking()
            .Where(o => o.CourierName != null)
            .Select(o => o.CourierName!)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
    }
}
