using EchoLifestyle.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// Base for every back-office controller.
///
/// Binding the authentication scheme here means a back-office controller
/// cannot accidentally be reachable with a storefront cookie: forgetting the
/// attribute is not possible, because the base class carries it.
/// </summary>
[Area("BackOffice")]
[Authorize(AuthenticationSchemes = AuthSchemes.BackOffice)]
public abstract class BackOfficeControllerBase : Controller
{
    /// <summary>Adds a toast to be shown after the next redirect.</summary>
    protected void Notify(string message, string type = "success")
    {
        TempData["ToastMessage"] = message;
        TempData["ToastType"] = type;
    }
}
