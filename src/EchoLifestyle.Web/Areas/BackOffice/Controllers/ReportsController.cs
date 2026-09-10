using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// The numbers, gated separately from the screens they come from.
///
/// A report shows the whole business at once - margin across every order, what
/// every courier owes - which is a different thing from being able to open one
/// order. Somebody who packs parcels needs the second and not the first.
/// </summary>
public class ReportsController : BackOfficeControllerBase
{
    private readonly SalesReportService _sales;
    private readonly InventoryReportService _inventory;
    private readonly SpendReportService _spending;
    private readonly IDateTimeProvider _clock;

    public ReportsController(
        SalesReportService sales,
        InventoryReportService inventory,
        SpendReportService spending,
        IDateTimeProvider clock)
    {
        _sales = sales;
        _inventory = inventory;
        _spending = spending;
        _clock = clock;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Reporting.SalesReportsView)]
    public IActionResult Index()
    {
        ViewData["Title"] = "Reports";

        return View();
    }

    /// <summary>
    /// Sales by day. Defaults to the last thirty days, which is the span
    /// somebody actually wants when they open a sales report without thinking
    /// about dates first.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Reporting.SalesReportsView)]
    public async Task<IActionResult> Sales(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var today = _clock.ToBusinessDate(_clock.UtcNow);

        var start = from ?? today.AddDays(-29);
        var end = to ?? today;

        ViewData["Title"] = "Sales";

        var summary = await _sales.GetSummaryAsync(start, end, cancellationToken);

        ViewData["TopProducts"] = await _sales.GetTopProductsAsync(
            start, end, take: 15, cancellationToken);

        return View(summary);
    }

    /// <summary>
    /// Money still with couriers. No date range: the question is always "what
    /// is outstanding right now", and a range would only let somebody hide the
    /// oldest debts by choosing the wrong one.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Reporting.FinancialReportsView)]
    public async Task<IActionResult> Outstanding(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Money owed";

        return View(await _sales.GetOutstandingAsync(cancellationToken));
    }

    /// <summary>
    /// Everything that goes out, and everything bought. Defaults to the current
    /// month, because spending is a question people ask a month at a time -
    /// unlike sales, which is asked about "the last few weeks".
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Reporting.FinancialReportsView)]
    public async Task<IActionResult> Spending(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var today = _clock.ToBusinessDate(_clock.UtcNow);

        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;

        ViewData["Title"] = "Money out";

        return View(await _spending.GetAsync(start, end, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Reporting.InventoryReportsView)]
    public async Task<IActionResult> Stock(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Stock";

        return View(await _inventory.GetAsync(cancellationToken));
    }
}
