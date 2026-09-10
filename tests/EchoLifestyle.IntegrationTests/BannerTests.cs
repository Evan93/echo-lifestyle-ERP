using EchoLifestyle.Application.Common.Files;
using EchoLifestyle.Application.Marketing;
using EchoLifestyle.Application.Storefront;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// The home page banner.
///
/// Most of what matters here is scheduling. A banner is the one piece of the
/// shop that is meant to stop being true on a particular date - a sale banner
/// still up a week after the sale ended advertises a price that is no longer
/// honoured, which is worse than showing nothing at all.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class BannerTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public BannerTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _fixture.CurrentUser.IsAuthenticated = true;
        _fixture.CurrentUser.UserType = UserType.Staff;
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.UserId = 1;
        _fixture.CurrentUser.UserName = "test-owner";
        _fixture.CurrentUser.Grants.Clear();

        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// A one-pixel PNG. The file store proves the bytes match the declared kind
    /// before it writes anything, so a test cannot get away with a text file
    /// labelled as an image - which is the point of that check.
    /// </summary>
    private static Stream Png() => new MemoryStream(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

    private static async Task<long> BannerAsync(
        IServiceProvider services,
        string name,
        bool isActive = true,
        DateTime? from = null,
        DateTime? to = null,
        int order = 0)
    {
        var banners = services.GetRequiredService<BannerAdminService>();

        await using var image = Png();

        var created = await banners.CreateAsync(
            new SaveBannerRequest
            {
                Name = name,
                AltText = "A test banner",
                IsActive = isActive,
                StartsAtUtc = from,
                EndsAtUtc = to,
                DisplayOrder = order,
            },
            image,
            FileKind.Png,
            null,
            null);

        Assert.True(created.Succeeded, created.Error);

        return created.Value;
    }

    /// <summary>Clears the table so ordering tests are not affected by leftovers.</summary>
    private static async Task ClearAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<EchoDbContext>();

        await db.Banners.ExecuteDeleteAsync();
    }

    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_active_banner_shows_on_the_home_page()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var storefront = services.GetRequiredService<StorefrontBannerService>();

        await ClearAsync(services);
        await BannerAsync(services, "Live one");

        var current = await storefront.GetCurrentAsync();

        Assert.Single(current);
        Assert.Equal("A test banner", current[0].AltText);
    }

    [Fact]
    public async Task Nothing_shows_when_there_is_nothing_to_announce()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;

        await ClearAsync(services);

        // Empty is an ordinary answer, not a failure. A shop with nothing to
        // say should show its products, not an empty grey box.
        Assert.Empty(await services.GetRequiredService<StorefrontBannerService>()
            .GetCurrentAsync());
    }

    [Fact]
    public async Task A_switched_off_banner_does_not_show()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;

        await ClearAsync(services);
        await BannerAsync(services, "Off", isActive: false);

        Assert.Empty(await services.GetRequiredService<StorefrontBannerService>()
            .GetCurrentAsync());
    }

    /// <summary>
    /// The reason scheduling exists. Both ends are checked, because the one
    /// that actually costs money is the end date: an expired sale banner is a
    /// promise the shop is no longer keeping.
    /// </summary>
    [Fact]
    public async Task A_banner_outside_its_window_does_not_show()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var storefront = services.GetRequiredService<StorefrontBannerService>();
        var now = DateTime.UtcNow;

        await ClearAsync(services);
        await BannerAsync(services, "Next month", from: now.AddDays(7));

        Assert.Empty(await storefront.GetCurrentAsync());

        await ClearAsync(services);
        await BannerAsync(services, "Last month", to: now.AddDays(-1));

        Assert.Empty(await storefront.GetCurrentAsync());

        await ClearAsync(services);
        await BannerAsync(services, "Right now", from: now.AddDays(-1), to: now.AddDays(1));

        Assert.Single(await storefront.GetCurrentAsync());
    }

    /// <summary>
    /// Order decides which slide leads, and the first slide is the one most
    /// people see - so it is the one the ordering has to get right.
    /// </summary>
    [Fact]
    public async Task The_lowest_display_order_leads()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;

        await ClearAsync(services);
        await BannerAsync(services, "Second", order: 5);
        await BannerAsync(services, "First", order: 1);

        var current = await services.GetRequiredService<StorefrontBannerService>()
            .GetCurrentAsync();

        var db = services.GetRequiredService<EchoDbContext>();
        var expected = await db.Banners.Where(b => b.DisplayOrder == 1)
            .Select(b => b.ImagePath).FirstAsync();

        Assert.Equal(2, current.Count);
        Assert.Equal(expected, current[0].ImagePath);
    }

    /// <summary>
    /// The cap is the whole argument for allowing a carousel at all. Every
    /// slide after the first is seen by fewer people, so a fourth costs a
    /// page-load's worth of image for an audience close to nobody.
    /// </summary>
    [Fact]
    public async Task No_more_than_three_slides_ever_reach_the_page()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;

        await ClearAsync(services);

        for (var i = 0; i < 6; i++)
        {
            await BannerAsync(services, $"Slide {i}", order: i);
        }

        var current = await services.GetRequiredService<StorefrontBannerService>()
            .GetCurrentAsync();

        Assert.Equal(StorefrontBannerService.MaxSlides, current.Count);
    }

    /// <summary>
    /// The back-office list computes "live now" the same way the storefront
    /// chooses, rather than by a second rule that could disagree with the site.
    /// </summary>
    [Fact]
    public async Task The_admin_list_agrees_with_what_the_website_is_showing()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var banners = services.GetRequiredService<BannerAdminService>();

        await ClearAsync(services);
        await BannerAsync(services, "Scheduled", order: 1, from: DateTime.UtcNow.AddDays(3));
        await BannerAsync(services, "Showing", order: 2);

        var list = await banners.ListAsync();
        var live = list.Where(b => b.IsLiveNow).ToList();

        Assert.Single(live);
        Assert.Equal("Showing", live[0].Name);


        // The scheduled one is active, and still not live. That distinction is
        // exactly what the badge in the list exists to make visible.
        Assert.True(list.Single(b => b.Name == "Scheduled").IsActive);
        Assert.False(list.Single(b => b.Name == "Scheduled").IsLiveNow);
    }

    /// <summary>
    /// A window that closes before it opens shows the banner never, silently.
    /// Refused rather than stored.
    /// </summary>
    [Fact]
    public async Task A_window_that_ends_before_it_starts_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var banners = services.GetRequiredService<BannerAdminService>();

        await using var image = Png();

        var result = await banners.CreateAsync(
            new SaveBannerRequest
            {
                Name = "Backwards",
                AltText = "Test",
                StartsAtUtc = DateTime.UtcNow.AddDays(5),
                EndsAtUtc = DateTime.UtcNow.AddDays(1),
            },
            image,
            FileKind.Png,
            null,
            null);

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// The link becomes an href on the home page, so anything that is not a
    /// path within this site or an http(s) URL is dropped rather than stored.
    /// A leading double slash matters: browsers read it as an absolute URL, so
    /// it looks internal and is not.
    /// </summary>
    [Fact]
    public async Task Only_a_real_destination_is_kept_as_the_link()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var banners = services.GetRequiredService<BannerAdminService>();

        await ClearAsync(services);

        async Task<string?> LinkAsync(string? typed)
        {
            await using var image = Png();

            var created = await banners.CreateAsync(
                new SaveBannerRequest
                {
                    Name = "Link test " + Guid.NewGuid().ToString("N")[..8],
                    AltText = "Test",
                    LinkUrl = typed,
                },
                image,
                FileKind.Png,
                null,
                null);

            Assert.True(created.Succeeded, created.Error);

            return (await banners.GetAsync(created.Value))!.LinkUrl;
        }

        Assert.Equal("/c/skincare", await LinkAsync("/c/skincare"));
        Assert.Equal("https://echolifestylebd.com", await LinkAsync("https://echolifestylebd.com"));

        Assert.Null(await LinkAsync("javascript:alert(1)"));
        Assert.Null(await LinkAsync("//evil.example/path"));
        Assert.Null(await LinkAsync("   "));
    }

    /// <summary>
    /// A banner appears on no document and carries no history, so unlike a
    /// product it really can be deleted rather than deactivated.
    /// </summary>
    [Fact]
    public async Task Deleting_a_banner_removes_it()
    {
        await using var scope = _fixture.CreateScope();
        var services = scope.ServiceProvider;
        var banners = services.GetRequiredService<BannerAdminService>();

        await ClearAsync(services);
        var id = await BannerAsync(services, "Temporary");

        Assert.True((await banners.DeleteAsync(id)).Succeeded);
        Assert.Null(await banners.GetAsync(id));
        Assert.Empty(await services.GetRequiredService<StorefrontBannerService>()
            .GetCurrentAsync());
    }
}
