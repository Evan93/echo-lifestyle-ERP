using EchoLifestyle.Domain.Security;

namespace EchoLifestyle.Application.Common.Interfaces;

/// <summary>
/// The acting user, resolved from the request. Application code depends on
/// this rather than on HttpContext so use cases remain unit-testable.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    long? UserId { get; }

    string? UserName { get; }

    /// <summary>Null when unauthenticated. Staff and Customer are hard-separated.</summary>
    UserType? UserType { get; }

    /// <summary>True only for a signed-in back-office user.</summary>
    bool IsStaff { get; }

    /// <summary>Branch ids this user may act in. Empty for customers.</summary>
    IReadOnlyCollection<long> BranchIds { get; }

    /// <summary>
    /// True when the user holds every permission. Owners bypass individual
    /// grants so a newly added permission never locks them out.
    /// </summary>
    bool IsOwner { get; }

    bool HasPermission(string permission);

    /// <summary>Branch-level data scoping check used by queries and commands.</summary>
    bool CanAccessBranch(long branchId);

    string? IpAddress { get; }

    string? TraceId { get; }
}
