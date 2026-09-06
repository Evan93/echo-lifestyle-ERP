using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Areas.BackOffice.Models.Users;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Authorize(Policy = Permissions.Security.UserView)]
public class UsersController : BackOfficeControllerBase
{
    private readonly UserAdminService _users;
    private readonly EchoDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UsersController(UserAdminService users, EchoDbContext db, ICurrentUser currentUser)
    {
        _users = users;
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        StatusFilter status,
        CancellationToken cancellationToken)
    {
        var page = await _users.ListAsync(
            request.NormalisedSearch,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            status,
            cancellationToken);

        return Json(GridResponse<StaffUserListItem>.From(request, page));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Security.UserCreate)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New staff account";

        var model = new UserFormModel();
        await PopulateOptionsAsync(model, cancellationToken);

        return View("Form", model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Security.UserCreate)]
    public async Task<IActionResult> Create(UserFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "New staff account";

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _users.CreateAsync(model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateOptionsAsync(model, cancellationToken);
            return View("Form", model);
        }

        Notify($"Staff account '{model.UserName}' created.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Security.UserEdit)]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var detail = await _users.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = detail.UserName;

        var model = UserFormModel.FromDetail(detail);
        model.IsSelf = detail.Id == _currentUser.UserId;
        await PopulateOptionsAsync(model, cancellationToken);

        return View("Form", model);
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Security.UserEdit)]
    public async Task<IActionResult> Edit(long id, UserFormModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        model.IsSelf = id == _currentUser.UserId;
        ViewData["Title"] = model.UserName;

        // Username is never editable, so a posted value is ignored rather than
        // trusted - the form field is disabled, and a disabled field is not a
        // control anyone has to respect.
        var existing = await _users.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        model.UserName = existing.UserName;
        ModelState.Remove(nameof(UserFormModel.UserName));

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View("Form", model);
        }

        var result = await _users.UpdateAsync(id, model.ToRequest(), cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            await PopulateOptionsAsync(model, cancellationToken);
            return View("Form", model);
        }

        Notify($"Staff account '{model.UserName}' saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Security.UserLock)]
    public async Task<IActionResult> Unlock(long id, CancellationToken cancellationToken)
    {
        var result = await _users.UnlockAsync(id, cancellationToken);

        Notify(
            result.Succeeded ? "Account unlocked." : result.Error!,
            result.Succeeded ? "success" : "danger");

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Security.UserResetPassword)]
    public async Task<IActionResult> ResetPassword(long id, CancellationToken cancellationToken)
    {
        var detail = await _users.GetAsync(id, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Reset password - {detail.UserName}";

        return View(new ResetPasswordModel { Id = detail.Id, UserName = detail.UserName });
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Security.UserResetPassword)]
    public async Task<IActionResult> ResetPassword(ResetPasswordModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = $"Reset password - {model.UserName}";

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _users.ResetPasswordAsync(model.Id, model.NewPassword, cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(result.Field ?? string.Empty, result.Error!);
            return View(model);
        }

        Notify($"Password reset for '{model.UserName}'. Tell them to change it after signing in.");
        return RedirectToAction(nameof(Edit), new { id = model.Id });
    }

    private async Task PopulateOptionsAsync(UserFormModel model, CancellationToken cancellationToken)
    {
        // From the database so custom roles appear here too - offering only the
        // seeded ones would make the role editor pointless.
        model.AvailableRoles = await _db.Roles
            .AsNoTracking()
            .Where(r => r.Name != null && r.Name != Roles.Customer)
            .OrderBy(r => r.Name)
            .Select(r => r.Name!)
            .ToListAsync(cancellationToken);

        model.AvailableBranches = await _db.Branches
            .AsNoTracking()
            .OrderBy(b => b.Code)
            .Select(b => new UserFormModel.BranchOption
            {
                Id = b.Id,
                Code = b.Code,
                Name = b.Name,
                IsActive = b.IsActive,
            })
            .ToListAsync(cancellationToken);
    }
}
