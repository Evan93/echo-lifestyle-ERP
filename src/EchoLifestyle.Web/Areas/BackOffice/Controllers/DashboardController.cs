using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Web.Areas.BackOffice.Controllers;

public class DashboardController : BackOfficeControllerBase
{
    private readonly EchoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public DashboardController(EchoDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Branch scoping in practice: owners see every branch, everyone else
        // sees only the branches assigned to them. The filter is applied in the
        // query, not in the view.
        var query = _db.Branches.AsNoTracking().Where(b => b.IsActive);

        if (!_currentUser.IsOwner)
        {
            var branchIds = _currentUser.BranchIds;
            query = query.Where(b => branchIds.Contains(b.Id));
        }

        var model = new DashboardViewModel
        {
            FullName = User.FindFirst(Security.EchoClaimTypes.FullName)?.Value ?? _currentUser.UserName ?? "-",
            UserName = _currentUser.UserName ?? "-",
            IsOwner = _currentUser.IsOwner,
            BusinessTime = _clock.BusinessNow,
            Branches = await query
                .OrderBy(b => b.Code)
                .Select(b => new BranchSummary
                {
                    Id = b.Id,
                    Code = b.Code,
                    Name = b.Name,
                    Type = b.Type,
                    AllowNegativeStock = b.AllowNegativeStock,
                    WarehouseCount = b.BranchWarehouses.Count,
                })
                .ToListAsync(cancellationToken),
        };

        return View(model);
    }
}

public class DashboardViewModel
{
    public string FullName { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public bool IsOwner { get; init; }

    public DateTimeOffset BusinessTime { get; init; }

    public IReadOnlyList<BranchSummary> Branches { get; init; } = [];
}

public class BranchSummary
{
    public long Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public BranchType Type { get; init; }

    public bool AllowNegativeStock { get; init; }

    public int WarehouseCount { get; init; }
}
