using System.Text.Json.Serialization;
using EchoLifestyle.Application;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Infrastructure;
using EchoLifestyle.Infrastructure.Files;
using EchoLifestyle.Infrastructure.Identity;
using EchoLifestyle.Infrastructure.Persistence;
using EchoLifestyle.Infrastructure.Persistence.Seed;
using EchoLifestyle.Web.Areas.BackOffice.Navigation;
using EchoLifestyle.Web.Middleware;
using EchoLifestyle.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});

app.UseStaticFiles();
app.UseSerilogRequestLogging();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

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
