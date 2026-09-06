using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Controllers;

[AllowAnonymous]
[Route("error")]
public class ErrorController : Controller
{
    [Route("{code:int}")]
    public IActionResult Index(int code, string? traceId = null)
    {
        var model = new ErrorViewModel
        {
            StatusCode = code,
            TraceId = traceId ?? Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            Message = code switch
            {
                403 => "You do not have permission to view this page.",
                404 => "That page could not be found.",
                _ => "Something went wrong while processing your request.",
            },
        };

        Response.StatusCode = code;
        return View(model);
    }
}

public class ErrorViewModel
{
    public int StatusCode { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>Quoted by the user to support; matches the id in the logs.</summary>
    public string TraceId { get; init; } = string.Empty;
}
