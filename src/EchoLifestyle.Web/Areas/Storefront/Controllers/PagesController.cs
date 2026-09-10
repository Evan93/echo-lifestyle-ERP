using EchoLifestyle.Application.Administration.CompanyProfile;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

/// <summary>
/// The pages a shop has to have: how delivery works, what happens if something
/// is wrong, what we do with your details, and how to reach a person.
///
/// Every one of them reads the company record rather than hard-coding a figure
/// or a phone number. A delivery page that says ৳60 in the markup is a page
/// that lies the day the charge changes, and it will change - which is the
/// whole reason those numbers are configuration.
/// </summary>
public class PagesController : StorefrontControllerBase
{
    private readonly CompanyAdminService _company;

    public PagesController(CompanyAdminService company)
    {
        _company = company;
    }

    [HttpGet]
    public Task<IActionResult> Delivery(CancellationToken cancellationToken) =>
        PageAsync("Delivery", cancellationToken);

    [HttpGet]
    public Task<IActionResult> Returns(CancellationToken cancellationToken) =>
        PageAsync("Returns", cancellationToken);

    [HttpGet]
    public Task<IActionResult> Privacy(CancellationToken cancellationToken) =>
        PageAsync("Privacy", cancellationToken);

    [HttpGet]
    public Task<IActionResult> Contact(CancellationToken cancellationToken) =>
        PageAsync("Contact", cancellationToken);

    private async Task<IActionResult> PageAsync(
        string view,
        CancellationToken cancellationToken)
    {
        var company = await _company.GetAsync(cancellationToken);

        if (company is null)
        {
            // Nothing has been set up. These pages are entirely about the
            // business's own details, so there is no useful page to render.
            return NotFound();
        }

        return View(view, company);
    }
}
