using System.Text.Json.Serialization;
using EchoLifestyle.Application;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Infrastructure;
using EchoLifestyle.Infrastructure.Files;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Infrastructure.Persistence.Seed;
using EchoLifestyle.Web.Areas.BackOffice.Navigation;
using EchoLifestyle.Web.Areas.Storefront.Controllers;
using EchoLifestyle.Web.Middleware;
using EchoLifestyle.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging - structured, with a rolling file so production issues are diagnosable
// ---------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// ---------------------------------------------------------------------------
// Persistence
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' is not configured. Set ConnectionStrings:Default in appsettings or user-secrets.");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString, builder.Environment.IsDevelopment());
builder.Services.AddScoped<DbSeeder>();
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));

// ---------------------------------------------------------------------------
// Uploaded files
//
// The root is set here rather than in configuration because only the host knows
// where its web root is. Anything else about the store - the size cap, the URL
// prefix - can be overridden from appsettings.
// ---------------------------------------------------------------------------
builder.Services
    .AddOptions<FileStorageOptions>()
    .Bind(builder.Configuration.GetSection(FileStorageOptions.SectionName))
    .PostConfigure(options => options.RootPath = builder.Environment.WebRootPath);

// ---------------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------------
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 4;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<EchoDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

// Permissions travel in the sign-in cookie, so a permission change would
// otherwise not take effect until the person signed out - a security control
// failing in the wrong direction. The security stamp makes a cookie stale:
// changing a role's permissions bumps the stamp of everyone holding it, and
// the validator rebuilds their claims from the database on the next request.
builder.Services.AddScoped<ISecurityStampValidator, SecurityStampValidator<ApplicationUser>>();

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    // How stale a cookie may be before it is revalidated. Two minutes keeps the
    // database work negligible while making a revoked permission take effect
    // fast enough to be useful.
    options.ValidationInterval = TimeSpan.FromMinutes(2);
});

// ---------------------------------------------------------------------------
// Authentication - two independent cookies.
//
// A storefront session is not a credential for the back office: the admin
// scheme simply does not recognise the shopper cookie, so such a request is
// anonymous there before any permission check runs.
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(AuthSchemes.BackOffice)
    .AddCookie(AuthSchemes.BackOffice, options =>
    {
        options.Cookie.Name = AuthSchemes.BackOfficeCookie;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/backoffice/account/login";
        options.LogoutPath = "/backoffice/account/logout";
        options.AccessDeniedPath = "/backoffice/account/denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Revalidates the security stamp, so role and permission changes reach
        // an already-signed-in user without them having to sign out.
        options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
    })
    .AddCookie(AuthSchemes.Storefront, options =>
    {
        options.Cookie.Name = AuthSchemes.StorefrontCookie;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/denied";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
    });

// ---------------------------------------------------------------------------
// Authorization - policies are generated per permission on demand
// ---------------------------------------------------------------------------
// ---------------------------------------------------------------------------
// Rate limits for the anonymous storefront
//
// These endpoints create real rows without anybody signing in: a basket, a
// customer, an order. Partitioned by IP, which is imperfect behind a shared
// mobile gateway - so checkout's ceiling is set where a family on one
// connection is comfortable and a script is not.
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter(RateLimits.Cart, limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter(RateLimits.Checkout, limiter =>
    {
        // Ten orders an hour from one address. A household ordering together
        // never reaches it; anything filling the customer table does.
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromHours(1);
        limiter.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter(RateLimitPolicies.Login, limiter =>
    {
        // Identity already locks an account after repeated failures, which
        // stops one account being hammered. It does nothing about the opposite
        // shape of attack - one password tried against many usernames - because
        // no single account ever fails twice. This is the ceiling for that.
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(5);
        limiter.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter(RateLimits.Tracking, limiter =>
    {
        // Twenty lookups in ten minutes. A customer checking on two parcels and
        // mistyping a phone number never reaches it. A script working through
        // order numbers hits it in seconds, which is the only thing keeping
        // "order number plus phone" from being brute-forceable.
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(10);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Sidebar is built per request and filtered by the signed-in user's permissions.
builder.Services.AddScoped<BackOfficeMenu>();

// ---------------------------------------------------------------------------
// MVC
// ---------------------------------------------------------------------------
builder.Services.AddControllersWithViews(options =>
{
    // Every state-changing request must carry an anti-forgery token, including
    // AJAX posts. Opting out has to be explicit and visible in the code.
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
})
.AddJsonOptions(options =>
{
    // Enums travel as their names. Grids and any future API consumer read
    // "Online" rather than 1, which survives someone reordering an enum.
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddRouting(options => options.LowercaseUrls = true);

var app = builder.Build();

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseStaticFiles();
app.UseSerilogRequestLogging();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseStatusCodePagesWithReExecute("/error/{0}");

// ---------------------------------------------------------------------------
// Routes
//
// The back office is pinned to its own literal prefix rather than the usual
// {area:exists} pattern. With two areas, {area:exists} would also answer the
// storefront at /Storefront/Catalog/Product?slug=x - a second URL for every
// page, which search engines treat as duplicate content and which nothing
// would ever link to on purpose.
//
// Storefront URLs are short on purpose: they get typed, shared in Messenger
// and printed on packaging.
// ---------------------------------------------------------------------------

app.MapAreaControllerRoute(
    name: "backoffice",
    areaName: "BackOffice",
    pattern: "BackOffice/{controller=Dashboard}/{action=Index}/{id?}");

app.MapAreaControllerRoute(
    name: "storefront-product",
    areaName: "Storefront",
    pattern: "p/{slug}",
    defaults: new { controller = "Catalog", action = "Product" });

app.MapAreaControllerRoute(
    name: "storefront-category",
    areaName: "Storefront",
    pattern: "c/{slug}",
    defaults: new { controller = "Catalog", action = "Category" });

app.MapAreaControllerRoute(
    name: "storefront-brand",
    areaName: "Storefront",
    pattern: "b/{slug}",
    defaults: new { controller = "Catalog", action = "Brand" });

app.MapAreaControllerRoute(
    name: "storefront-cart",
    areaName: "Storefront",
    pattern: "cart",
    defaults: new { controller = "Cart", action = "Index" });

app.MapAreaControllerRoute(
    name: "storefront-cart-add",
    areaName: "Storefront",
    pattern: "cart/add",
    defaults: new { controller = "Cart", action = "Add" });

app.MapAreaControllerRoute(
    name: "storefront-cart-update",
    areaName: "Storefront",
    pattern: "cart/update",
    defaults: new { controller = "Cart", action = "Update" });

app.MapAreaControllerRoute(
    name: "storefront-cart-remove",
    areaName: "Storefront",
    pattern: "cart/remove",
    defaults: new { controller = "Cart", action = "Remove" });

app.MapAreaControllerRoute(
    name: "storefront-delivery-quote",
    areaName: "Storefront",
    pattern: "checkout/delivery",
    defaults: new { controller = "Checkout", action = "Delivery" });

app.MapAreaControllerRoute(
    name: "storefront-checkout",
    areaName: "Storefront",
    pattern: "checkout",
    defaults: new { controller = "Checkout", action = "Index" });

app.MapAreaControllerRoute(
    name: "storefront-order-placed",
    areaName: "Storefront",
    pattern: "order-received",
    defaults: new { controller = "Checkout", action = "Done" });

app.MapAreaControllerRoute(
    name: "storefront-search",
    areaName: "Storefront",
    pattern: "search",
    defaults: new { controller = "Catalog", action = "Search" });

// The fixed menu entries. These work on day one with no category tree behind
// them, which is precisely when a shop most needs somewhere to send people.
app.MapAreaControllerRoute(
    name: "storefront-new",
    areaName: "Storefront",
    pattern: "new",
    defaults: new { controller = "Catalog", action = "New" });

app.MapAreaControllerRoute(
    name: "storefront-offers",
    areaName: "Storefront",
    pattern: "offers",
    defaults: new { controller = "Catalog", action = "Offers" });

app.MapAreaControllerRoute(
    name: "storefront-brands",
    areaName: "Storefront",
    pattern: "brands",
    defaults: new { controller = "Catalog", action = "Brands" });

// The pages a shop has to have. Named routes rather than the default
// {controller}/{action} pattern for the same reason as the catalogue: these
// URLs get printed, linked to from Facebook, and never want to change.
app.MapAreaControllerRoute(
    name: "storefront-track",
    areaName: "Storefront",
    pattern: "track",
    defaults: new { controller = "Track", action = "Index" });

// Both live at the site root because that is the only place a crawler looks
// for them. A sitemap at /Storefront/Seo/Sitemap is a sitemap nobody reads.
app.MapAreaControllerRoute(
    name: "storefront-sitemap",
    areaName: "Storefront",
    pattern: "sitemap.xml",
    defaults: new { controller = "Seo", action = "Sitemap" });

app.MapAreaControllerRoute(
    name: "storefront-robots",
    areaName: "Storefront",
    pattern: "robots.txt",
    defaults: new { controller = "Seo", action = "Robots" });

app.MapAreaControllerRoute(
    name: "storefront-delivery-page",
    areaName: "Storefront",
    pattern: "delivery",
    defaults: new { controller = "Pages", action = "Delivery" });

app.MapAreaControllerRoute(
    name: "storefront-returns",
    areaName: "Storefront",
    pattern: "returns",
    defaults: new { controller = "Pages", action = "Returns" });

app.MapAreaControllerRoute(
    name: "storefront-privacy",
    areaName: "Storefront",
    pattern: "privacy",
    defaults: new { controller = "Pages", action = "Privacy" });

app.MapAreaControllerRoute(
    name: "storefront-contact",
    areaName: "Storefront",
    pattern: "contact",
    defaults: new { controller = "Pages", action = "Contact" });

app.MapAreaControllerRoute(
    name: "storefront-home",
    areaName: "Storefront",
    pattern: "",
    defaults: new { controller = "Home", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// ---------------------------------------------------------------------------
// Health
//
// Two endpoints, because they answer different questions and a monitor that
// cannot tell them apart will restart a healthy site.
//
//   /health       is the process alive? Cheap, no dependencies, safe to poll
//                 every few seconds.
//   /health/ready can it actually serve? Touches the database.
//
// A host that restarts the application because SQL Server is briefly
// unreachable has turned a database blip into an outage - so the liveness
// check deliberately knows nothing about the database.
//
// Neither reveals anything: a bare word and a status code. A health endpoint
// that returns exception detail is a reconnaissance tool.
// ---------------------------------------------------------------------------
app.MapGet("/health", () => Results.Text("ok")).AllowAnonymous();

app.MapGet("/health/ready", async (EchoDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Text("ready")
        : Results.Text("not ready", statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

// ---------------------------------------------------------------------------
// Startup: migrate (development only) and seed
//
// Migrations are NOT applied automatically outside Development. Schema changes
// in production are a deliberate, controlled deployment step.
// ---------------------------------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        var db = services.GetRequiredService<EchoDbContext>();

        if (app.Environment.IsDevelopment())
        {
            logger.LogInformation("Applying pending migrations (Development)...");
            await db.Database.MigrateAsync();
        }
        else if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            logger.LogWarning(
                "There are pending migrations. Apply them as part of deployment before serving traffic.");
        }

        var seeder = services.GetRequiredService<DbSeeder>();
        var seedOptions = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        await seeder.SeedAsync(seedOptions, app.Environment.IsDevelopment());
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database initialisation failed. The application will not start.");
        throw;
    }
}

app.Run();

/// <summary>Exposed so the integration tests can host the application.</summary>
public partial class Program;
