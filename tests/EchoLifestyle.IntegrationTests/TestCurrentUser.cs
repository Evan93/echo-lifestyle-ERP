using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Security;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Stands in for the signed-in user when there is no HTTP request - the audit
/// interceptor needs one during seeding and direct database tests.
/// </summary>
public class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; }

    public long? UserId { get; set; }

    public string? UserName { get; set; }

    public UserType? UserType { get; set; }

    public bool IsStaff => IsAuthenticated && UserType == Domain.Security.UserType.Staff;

    public IReadOnlyCollection<long> BranchIds { get; set; } = [];

    public bool IsOwner { get; set; }

    public bool HasPermission(string permission) => IsOwner;

    public bool CanAccessBranch(long branchId) => IsOwner || BranchIds.Contains(branchId);

    public string? IpAddress => null;

    public string? TraceId => "test";
}
