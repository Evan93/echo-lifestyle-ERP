namespace EchoLifestyle.Web.Middleware;

/// <summary>
/// The headers that tell a browser what this site is and is not allowed to do.
///
/// Moved out of a lambda in Program.cs once the content security policy arrived,
/// because a policy is a thing that needs explaining and a lambda is a poor
/// place to explain it.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _contentSecurityPolicy;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _contentSecurityPolicy = BuildPolicy(environment.IsDevelopment());
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        // Nothing here uses a camera, a microphone or a location, so nothing
        // here should be able to ask for one - including a script that got onto
        // the page by a route nobody intended.
        headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";

        headers["Content-Security-Policy"] = _contentSecurityPolicy;

        await _next(context);
    }

    /// <summary>
    /// What may load, and from where.
    ///
    /// Applied in development as well as production, deliberately. A policy
    /// that is only switched on in production is a policy first exercised by
    /// customers, and the failure mode - a silently blocked script - is one
    /// nobody notices until the numbers look wrong a fortnight later.
    ///
    /// <para>
    /// <c>script-src</c> still carries <c>'unsafe-inline'</c>, and that is the
    /// honest limit of this policy: several pages carry inline scripts, so
    /// injected inline script is not blocked here. Everything else in the list
    /// is real protection and worth having on its own:
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><c>default-src 'self'</c> - an injected tag cannot pull code from
    /// a host that is not named below.</item>
    /// <item><c>form-action 'self'</c> - a form cannot be repointed to post
    /// somewhere else. On a checkout that carries names, phone numbers and
    /// addresses, this is the single most valuable line here.</item>
    /// <item><c>base-uri 'self'</c> - an injected &lt;base&gt; tag cannot
    /// silently redirect every relative URL on the page.</item>
    /// <item><c>frame-ancestors 'none'</c> - the site cannot be framed, which
    /// is what stops a clickjacked "confirm order" button.</item>
    /// <item><c>object-src 'none'</c> - no plugins, ever.</item>
    /// </list>
    ///
    /// <para>
    /// Removing <c>'unsafe-inline'</c> means giving every inline script a
    /// per-request nonce. That is the next step and it is worth taking, but it
    /// touches every view with a script in it and cannot be verified from here
    /// - so it is a deliberate follow-up rather than a change made blind.
    /// </para>
    /// </summary>
    private static string BuildPolicy(bool isDevelopment)
    {
        // The analytics vendors. Both inject further scripts of their own once
        // loaded, which is why their own hosts have to be named as well as the
        // one the snippet points at.
        const string MetaScript = "https://connect.facebook.net";
        const string GoogleScript = "https://www.googletagmanager.com";

        // Where the page may send data. Narrow on purpose: this is the
        // directive that would stop an injected script quietly posting a basket
        // full of customer details somewhere else.
        //
        // Built once rather than added and then patched: two connect-src
        // directives in one policy means the browser honours the first and
        // silently ignores the second, which is a very quiet way to end up with
        // a policy that is not the one you read.
        var connect = "connect-src 'self' https://www.facebook.com https://connect.facebook.net "
            + "https://www.google-analytics.com https://*.google-analytics.com "
            + "https://*.analytics.google.com https://*.googletagmanager.com";

        if (isDevelopment)
        {
            // The hot-reload channel. Without it the console fills with
            // violations on every page and the real ones get lost among them.
            connect += " ws: wss: http://localhost:* https://localhost:*";
        }

        var directives = new List<string>
        {
            "default-src 'self'",

            $"script-src 'self' 'unsafe-inline' {MetaScript} {GoogleScript}",

            // Bootstrap and several views set style attributes inline. Style
            // injection is a far smaller problem than script injection, and
            // nonce-ing every style attribute is not a trade worth making.
            "style-src 'self' 'unsafe-inline'",

            // data: for the placeholder and icon images that are embedded
            // rather than fetched; the vendor hosts serve tracking pixels.
            "img-src 'self' data: https://www.facebook.com https://www.google-analytics.com "
            + "https://*.googletagmanager.com https://*.google-analytics.com",

            "font-src 'self' data:",

            connect,

            "frame-src 'none'",
            "frame-ancestors 'none'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
        };

        if (!isDevelopment)
        {
            // Outside development only: on a local HTTP run this would break
            // every static file on the page.
            directives.Add("upgrade-insecure-requests");
        }

        return string.Join("; ", directives);
    }
}
