using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Web.Areas.BackOffice.Models.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Authorize(Policy = Permissions.Security.RoleView)]
public class RolesController : BackOfficeControllerBase
{
    private readonly RoleAdminService _roles;

    public RolesController(RoleAdminService roles)
    {
        _roles = roles;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var roles = await _roles.ListAsync(cancellationToken);
        return View(roles);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Security.RoleEdit)]
    public IActionResult Create()
    {
        ViewData["Title"] = "New role";
        return View("Form", new RoleFormModel());
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Security.RoleEdit)]
    public async Task<IActionResult> Create(RoleFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New role";

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var result = await _roles.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View("Form", model);
        }

        Notify($"Role '{model.Name}' created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Security.RoleEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _roles.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.Name;
        return View("Form", RoleFormModel.FromDetail(detail));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Security.RolePermissionsEdit)]
    public async Task<IActionResult> Edit(long id, RoleFormModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        ViewData["Title"] = model.Name;

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var result = await _roles.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);

            // Restore the flags the form needs; they are not posted back.
            var current = await _roles.GetAsync(id, cancellationToken);
            if (current is not null)
            {
                model.IsSystemRole = current.IsSystemRole;
                model.PermissionsAreFixed = current.PermissionsAreFixed;
                model.UserCount = current.UserCount;
            }

            return View("Form", model);
        }

        Notify($"Role '{model.Name}' saved. Anyone holding it picks up the change within two minutes.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Security.RoleEdit)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var result = await _roles.DeleteAsync(id, cancellationToken);

        Notify(
            result.Succeeded ? "Role deleted." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Index));
    }
}
