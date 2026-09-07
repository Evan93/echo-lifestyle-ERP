using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Finance.Cash;
using EchoLifestyle.Application.Purchasing.Suppliers;
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
/// The money log, and everything written into it by hand.
///
/// One screen for what is in each pot, one for where it went, one for what the
/// partners have put in - all three reading the same append-only log that a
/// courier payout writes to. Nothing here edits an entry: a mistake is
/// cancelled by a reversing one, which is the only way out of a log you can
/// trust.
/// </summary>
[Authorize(Policy = Permissions.Finance.CashView)]
public class CashController : BackOfficeControllerBase
{
    private readonly CashService _cash;
    private readonly ExpenseCategoryService _categories;
    private readonly PartnerService _partners;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly EchoDbContext _db;

    public CashController(
        CashService cash,
        ExpenseCategoryService categories,
        PartnerService partners,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        EchoDbContext db)
    {
        _cash = cash;
        _categories = categories;
        _partners = partners;
        _currentUser = currentUser;
        _clock = clock;
        _db = db;
    }

    // -----------------------------------------------------------------------
    // The log
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Index(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var (start, end) = Period(from, to);

        ViewData["Position"] = await _cash.GetPositionAsync(start, end, cancellationToken);
        ViewData["From"] = start;
        ViewData["To"] = end;
        ViewData["CanRecord"] = _currentUser.HasPermission(Permissions.Finance.CashRecord);

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        CashKind? kind,
        CashDirection? direction,
        PaymentMethodKind? method,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var page = await _cash.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            kind,
            direction,
            method,
            from,
            to,
            cancellationToken);

        return Json(GridResponse<CashListItem>.From(request, page));
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var detail = await _cash.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Number;
        ViewData["CanReverse"] = _currentUser.HasPermission(Permissions.Finance.CashReverse);

        return View(detail);
    }

    // -----------------------------------------------------------------------
    // Writing to it
    // -----------------------------------------------------------------------

    [HttpGet]
    [Authorize(Policy = Permissions.Finance.CashRecord)]
    public async Task<IActionResult> Record(
        CashKind? kind,
        string? order,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Record money";

        var model = new RecordCashFormModel
        {
            Kind = kind ?? CashKind.Expense,
            OrderNumber = order,
            TransactionDate = _clock.ToBusinessDate(_clock.UtcNow),
        };

        await PopulateAsync(model, cancellationToken);

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Finance.CashRecord)]
    public async Task<IActionResult> Record(
        RecordCashFormModel model,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Record money";

        if (!ModelState.IsValid)
        {
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        var result = await _cash.RecordAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateAsync(model, cancellationToken);
            return View(model);
        }

        Notify("Recorded.");
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Finance.CashReverse)]
    public async Task<IActionResult> Reverse(
        long id,
        string? reason,
        CancellationToken cancellationToken)
    {
        var result = await _cash.ReverseAsync(id, reason, cancellationToken);

        if (!result.Succeeded)
        {
            Notify(result.Error!, "danger");
            return RedirectToAction(nameof(Details), new { id });
        }

        Notify("Reversed. Both entries stay on the log.", "warning");
        return RedirectToAction(nameof(Details), new { id = result.Value });
    }

    // -----------------------------------------------------------------------
    // Where it went
    // -----------------------------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Expenses(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Expenses";

        var (start, end) = Period(from, to);

        return View(await _cash.GetExpenseBreakdownAsync(start, end, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Finance.PartnerLedgerView)]
    public async Task<IActionResult> Partners(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Partner capital";
        ViewData["CanRecord"] = _currentUser.HasPermission(Permissions.Finance.CashRecord);

        return View(await _cash.GetPartnerLedgerAsync(cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Finance.PartnerLedgerView)]
    [Authorize(Policy = Permissions.Finance.CashRecord)]
    public async Task<IActionResult> SavePartner(
        PartnerFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notify("Check the partner details.", "danger");
            return RedirectToAction(nameof(Partners));
        }

        var result = await _partners.SaveAsync(model.ToRequest(), cancellationToken);

        Notify(result.Succeeded ? "Saved." : result.Error!, result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Partners));
    }

    // -----------------------------------------------------------------------
    // Categories
    // -----------------------------------------------------------------------

    [HttpGet]
    [Authorize(Policy = Permissions.Finance.ExpenseCategoryEdit)]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Expense categories";

        return View(await _categories.ListAsync(cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Finance.ExpenseCategoryEdit)]
    public async Task<IActionResult> SaveCategory(
        ExpenseCategoryFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Notify("Check the category details.", "danger");
            return RedirectToAction(nameof(Categories));
        }

        var result = await _categories.SaveAsync(model.ToRequest(), cancellationToken);

        Notify(result.Succeeded ? "Saved." : result.Error!, result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Categories));
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// The period a screen defaults to: this calendar month in Dhaka. A cash
    /// position with no date range is meaningless, and "since the beginning" is
    /// not the question anybody opens this screen with.
    /// </summary>
    private (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to)
    {
        var today = _clock.ToBusinessDate(_clock.UtcNow);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;

        return end < start ? (end, start) : (start, end);
    }

    private async Task PopulateAsync(
        RecordCashFormModel model,
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

        model.Categories = await _cash.GetExpenseCategoriesAsync(cancellationToken);

        model.CanRecordPartnerMoney = _currentUser.IsOwner
            || _currentUser.HasPermission(Permissions.Finance.PartnerLedgerView);

        model.Partners = model.CanRecordPartnerMoney
            ? await _cash.GetPartnersAsync(cancellationToken)
            : [];

        model.Suppliers = await _db.Suppliers
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new SupplierOption
            {
                Id = s.Id,
                Code = s.Code,
                Name = s.Name,
                IsActive = s.IsActive,
            })
            .ToListAsync(cancellationToken);
    }
}
