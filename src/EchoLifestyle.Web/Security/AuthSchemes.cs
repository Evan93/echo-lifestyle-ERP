namespace EchoLifestyle.Web.Security;

/// <summary>
/// Back-office and storefront use separate cookies and separate authentication
/// schemes. A shopper's session is therefore not a valid credential for admin
/// routes at all - the request is anonymous there, before any permission check
/// even runs.
/// </summary>
public static class AuthSchemes
{
    public const string BackOffice = "EchoBackOffice";
    public const string Storefront = "EchoStorefront";

    public const string BackOfficeCookie = "echo.admin";
    public const string StorefrontCookie = "echo.shop";
}
