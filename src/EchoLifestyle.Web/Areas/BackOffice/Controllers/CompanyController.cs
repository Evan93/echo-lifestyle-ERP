using EchoLifestyle.Application.Administration.CompanyProfile;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Web.Areas.BackOffice.Models.Company;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Authorize(Policy = Permissions.Administration.CompanyView)]
public class CompanyController : BackOfficeControllerBase
{
    private readonly CompanyAdminService _company;

    public CompanyController(CompanyAdminService company)
    {
        _company = company;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var detail = await _company.GetAsync(cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        return View(CompanyFormModel.FromDetail(detail));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Administration.CompanyEdit)]
    public async Task<IActionResult> Index(CompanyFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var wasRegistered = (await _company.GetAsync(cancellationToken))?.IsVatRegistered ?? false;

        var result = await _company.UpdateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View(model);
        }

        // Registering for VAT changes how invoices are produced, so say so
        // rather than letting it pass as an ordinary save.
        Notify(!wasRegistered && model.IsVatRegistered
            ? "Company profile saved. VAT registration recorded - Mushak-compliant invoicing is now active."
            : "Company profile saved.");

        return RedirectToAction(nameof(Index));
    }
}
