using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Web.Areas.BackOffice.Models;
using EchoLifestyle.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

[Area("BackOffice")]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IUserClaimsPrincipalFactory<ApplicationUser> _claimsFactory;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly EchoDbContext _db;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IAuditLogger audit,
        IDateTimeProvider clock,
        EchoDbContext db,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _claimsFactory = claimsFactory;
        _audit = audit;
        _clock = clock;
        _db = db;
        _logger = logger;
    }

    // Anonymous access is granted per action rather than on the controller.
    // A class-level [AllowAnonymous] would silently override the [Authorize]
    // on Logout, leaving it reachable without a session.
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByNameAsync(model.UserName);

        // Every rejection below returns the same message. Telling the caller
        // whether the username exists, is inactive, or is a customer account
        // would hand an attacker a user-enumeration oracle.
        const string GenericFailure = "Incorrect username or password.";

        if (user is null || user.UserType != UserType.Staff || !user.IsActive)
        {
            await LogFailureAsync(model.UserName, "unknown, inactive, or non-staff account", cancellationToken);
            ModelState.AddModelError(string.Empty, GenericFailure);
            return View(model);
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await _audit.LogAsync(
                AuditActions.LoginLockedOut,
                nameof(ApplicationUser),
                user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Account '{user.UserName}' is locked out.",
                cancellationToken: cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            ModelState.AddModelError(
                string.Empty,
                "This account is temporarily locked after too many failed attempts. Try again later.");

            return View(model);
        }

        if (!result.Succeeded)
        {
            await LogFailureAsync(model.UserName, "incorrect password", cancellationToken);
            ModelState.AddModelError(string.Empty, GenericFailure);
            return View(model);
        }

        // Signed in explicitly rather than through SignInManager, so the cookie
        // lands on the back-office scheme and never on the storefront one.
        var principal = await _claimsFactory.CreateAsync(user);

        var properties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            IssuedUtc = _clock.UtcNow,
        };

        await HttpContext.SignInAsync(AuthSchemes.BackOffice, principal, properties);

        user.LastLoginAtUtc = _clock.UtcNow;

        await _audit.LogAsync(
            AuditActions.LoginSucceeded,
            nameof(ApplicationUser),
            user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"'{user.UserName}' signed in to the back office.",
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Back-office sign-in for {UserName}", user.UserName);

        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = AuthSchemes.BackOffice)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSchemes.BackOffice);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Denied() => View();

    private async Task LogFailureAsync(string userName, string reason, CancellationToken cancellationToken)
    {
        await _audit.LogAsync(
            AuditActions.LoginFailed,
            nameof(ApplicationUser),
            entityId: null,
            summary: $"Failed back-office sign-in for '{userName}'.",
            detail: new { userName, reason },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Failed back-office sign-in for {UserName}: {Reason}", userName, reason);
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        // Never redirect to an absolute URL supplied by the caller - that is an
        // open-redirect handed to a phishing page.
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Dashboard", new { area = "BackOffice" });
    }
}
