using EchoLifestyle.Application.Administration.Auditing;
using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Web.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

/// <summary>
/// The audit trail, read-only by design. There is no action here that can
/// change or remove an entry - a trail anyone can edit is not evidence.
/// </summary>
[Authorize(Policy = Permissions.Security.AuditLogView)]
public class AuditController : BackOfficeControllerBase
{
    private readonly AuditQueryService _audit;

    public AuditController(AuditQueryService audit)
    {
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["RecordedActions"] = await _audit.GetRecordedActionsAsync(cancellationToken);
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Data(
        GridRequest request,
        AuditFilter filter,
        CancellationToken cancellationToken)
    {
        var page = await _audit.ListAsync(
            filter,
            request.Skip,
            request.PageSize,
            request.SortColumn,
            request.SortDescending,
            cancellationToken);

        return Json(GridResponse<AuditListItem>.From(request, page));
    }
}
