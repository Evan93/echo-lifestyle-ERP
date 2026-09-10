namespace EchoLifestyle.Web.Security;

/// <summary>
/// Rate-limit policies for the back office.
///
/// Separate from the storefront's <c>RateLimits</c> because the two protect
/// different things for different reasons: the storefront's ceilings stop an
/// anonymous script filling the database with baskets and orders, and this one
/// stops somebody working through passwords.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// The sign-in form.
    ///
    /// Identity's own lockout already stops one account being hammered. What it
    /// cannot see is the opposite attack - one likely password tried against a
    /// list of usernames - because no individual account ever fails twice, so no
    /// lockout ever triggers. A per-address ceiling is what closes that.
    /// </summary>
    public const string Login = "backoffice-login";
}
