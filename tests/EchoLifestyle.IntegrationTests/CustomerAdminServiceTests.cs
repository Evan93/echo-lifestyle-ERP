using EchoLifestyle.Application.Common.Authorization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Domain.Crm;
using EchoLifestyle.Domain.Security;
using EchoLifestyle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// Customers.
///
/// The tests that matter here are all one question in different clothes: does
/// the same person, reaching the business three different ways, end up as one
/// customer with one history?
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CustomerAdminServiceTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public CustomerAdminServiceTests(DatabaseFixture fixture)
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

    public Task DisposeAsync()
    {
        _fixture.CurrentUser.IsOwner = true;
        _fixture.CurrentUser.Grants.Clear();

        return Task.CompletedTask;
    }

    /// <summary>
    /// A number nobody else in the suite will use. Operator prefix stays valid;
    /// the last eight digits are the unique part.
    /// </summary>
    private static string UniquePhone() =>
        "017" + Random.Shared.Next(10_000_000, 99_999_999).ToString("D8");

    private static SaveCustomerRequest Request(string phone, string? name = null) => new()
    {
        FullName = name ?? $"Test customer {Guid.NewGuid().ToString("N")[..6]}",
        Phone = phone,
        Source = CustomerSource.Messenger,
        IsActive = true,
    };

    private async Task<long> DistrictAsync(IServiceProvider services, string name = "Dhaka")
    {
        var db = services.GetRequiredService<EchoDbContext>();
        return await db.Districts.Where(d => d.Name == name).Select(d => d.Id).FirstAsync();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_customer_is_stored_under_the_normalised_number_whatever_was_typed()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var phone = UniquePhone();

        var created = await customers.CreateAsync(
            Request($"+880 {phone[1..5]}-{phone[5..]}"));

        Assert.True(created.Succeeded, created.Error);

        var stored = await db.Customers
            .Where(c => c.Id == created.Value)
            .Select(c => c.Phone)
            .FirstAsync();

        Assert.Equal(phone, stored);
    }

    [Fact]
    public async Task The_same_number_typed_differently_is_refused_as_a_duplicate()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var phone = UniquePhone();

        var first = await customers.CreateAsync(Request(phone, "Rima Akter"));
        Assert.True(first.Succeeded, first.Error);

        // The same person, reached through Instagram instead, with the number
        // pasted from the chat.
        var second = await customers.CreateAsync(Request($"+88{phone}", "Rima"));

        Assert.False(second.Succeeded);
        Assert.Equal(nameof(SaveCustomerRequest.Phone), second.Field);

        // The message names who already holds it, so the person typing can go
        // to that record instead of inventing a workaround.
        Assert.Contains("Rima Akter", second.Error!);
    }

    [Fact]
    public async Task A_number_that_is_not_a_bangladeshi_mobile_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var result = await customers.CreateAsync(Request("029551234"));

        Assert.False(result.Succeeded);
        Assert.Equal(nameof(SaveCustomerRequest.Phone), result.Field);
        Assert.Contains("013 to 019", result.Error!);
    }

    [Fact]
    public async Task Codes_are_sequential()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var first = await customers.CreateAsync(Request(UniquePhone()));
        var second = await customers.CreateAsync(Request(UniquePhone()));

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);

        var a = (await customers.GetAsync(first.Value))!.Code;
        var b = (await customers.GetAsync(second.Value))!.Code;

        Assert.StartsWith("CUS-", a);
        Assert.True(string.CompareOrdinal(b, a) > 0);
    }

    // -----------------------------------------------------------------------
    // Quick create - the fast path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Quick_create_returns_the_existing_customer_rather_than_failing()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var phone = UniquePhone();

        var original = await customers.CreateAsync(Request(phone, "Shirin"));
        Assert.True(original.Succeeded, original.Error);

        // Somebody typing an order does not want an error, they want the
        // customer. Reusing is what keeps duplicates out of the fast path.
        var quick = await customers.QuickCreateAsync(new QuickCreateCustomerRequest
        {
            FullName = "Shirin apa",
            Phone = $"+880{phone[1..]}",
        });

        Assert.True(quick.Succeeded, quick.Error);
        Assert.True(quick.Value!.WasExisting);
        Assert.Equal(original.Value, quick.Value.Id);

        // And it does not silently rename them to whatever was typed this time.
        Assert.Equal("Shirin", quick.Value.FullName);
    }

    [Fact]
    public async Task Quick_create_makes_a_new_customer_when_the_number_is_unknown()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var quick = await customers.QuickCreateAsync(new QuickCreateCustomerRequest
        {
            FullName = "Nusrat",
            Phone = UniquePhone(),
            Source = CustomerSource.Instagram,
        });

        Assert.True(quick.Succeeded, quick.Error);
        Assert.False(quick.Value!.WasExisting);
        Assert.StartsWith("CUS-", quick.Value.Code);
    }

    [Fact]
    public async Task Quick_create_still_needs_a_name()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var quick = await customers.QuickCreateAsync(new QuickCreateCustomerRequest
        {
            FullName = "  ",
            Phone = UniquePhone(),
        });

        Assert.False(quick.Succeeded);
    }

    // -----------------------------------------------------------------------
    // Addresses
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_first_address_becomes_the_default_whatever_the_form_said()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var created = await customers.CreateAsync(Request(UniquePhone()));
        var districtId = await DistrictAsync(scope.ServiceProvider);

        var saved = await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            DistrictId = districtId,
            AreaOrThana = "Bashundhara R/A",
            AddressLine = "House 42, Road 7",
            IsDefault = false,
        });

        Assert.True(saved.Succeeded, saved.Error);

        var address = Assert.Single((await customers.GetAsync(created.Value))!.Addresses);

        Assert.True(address.IsDefault);
    }

    [Fact]
    public async Task Promoting_a_second_address_demotes_the_first()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var created = await customers.CreateAsync(Request(UniquePhone()));
        var dhaka = await DistrictAsync(scope.ServiceProvider);
        var chattogram = await DistrictAsync(scope.ServiceProvider, "Chattogram");

        await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            Label = "Home",
            DistrictId = dhaka,
            AreaOrThana = "Dhanmondi",
            AddressLine = "House 12, Road 3",
        });

        var second = await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            Label = "Office",
            DistrictId = chattogram,
            AreaOrThana = "Agrabad",
            AddressLine = "Level 4, Delwar Building",
            IsDefault = true,
        });

        Assert.True(second.Succeeded, second.Error);

        var detail = await customers.GetAsync(created.Value);
        var defaults = detail!.Addresses.Where(a => a.IsDefault).ToList();

        // Exactly one, or the order screen has to guess where a parcel goes.
        var only = Assert.Single(defaults);
        Assert.Equal("Office", only.Label);
    }

    [Fact]
    public async Task An_address_falls_back_to_the_customers_own_name_and_number()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var phone = UniquePhone();
        var created = await customers.CreateAsync(Request(phone, "Farhana Islam"));
        var districtId = await DistrictAsync(scope.ServiceProvider);

        await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            DistrictId = districtId,
            AreaOrThana = "Uttara Sector 7",
            AddressLine = "House 3, Road 15",
        });

        var address = Assert.Single((await customers.GetAsync(created.Value))!.Addresses);

        Assert.Equal("Farhana Islam", address.RecipientName);
        Assert.Equal(phone, address.RecipientPhone);
    }

    [Fact]
    public async Task An_address_can_be_delivered_to_somebody_else()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var created = await customers.CreateAsync(Request(UniquePhone(), "Tanvir"));
        var districtId = await DistrictAsync(scope.ServiceProvider);
        var recipientPhone = UniquePhone();

        await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            Label = "Ma's place",
            RecipientName = "Rokeya Begum",
            RecipientPhone = $"+880{recipientPhone[1..]}",
            DistrictId = districtId,
            AreaOrThana = "Mirpur 10",
            AddressLine = "House 8, Block C",
        });

        var address = Assert.Single((await customers.GetAsync(created.Value))!.Addresses);

        Assert.Equal("Rokeya Begum", address.RecipientName);
        Assert.Equal(recipientPhone, address.RecipientPhone);
    }

    [Fact]
    public async Task An_address_without_a_district_is_refused()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var created = await customers.CreateAsync(Request(UniquePhone()));

        var saved = await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            DistrictId = 0,
            AreaOrThana = "Somewhere",
            AddressLine = "A house",
        });

        Assert.False(saved.Succeeded);
        Assert.Contains("district", saved.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Removing_the_default_address_promotes_another()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var created = await customers.CreateAsync(Request(UniquePhone()));
        var districtId = await DistrictAsync(scope.ServiceProvider);

        var first = await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            Label = "Home",
            DistrictId = districtId,
            AreaOrThana = "Banani",
            AddressLine = "House 1",
        });

        await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            Label = "Office",
            DistrictId = districtId,
            AreaOrThana = "Gulshan 1",
            AddressLine = "House 2",
        });

        var removed = await customers.DeleteAddressAsync(created.Value, first.Value);
        Assert.True(removed.Succeeded, removed.Error);

        var remaining = Assert.Single((await customers.GetAsync(created.Value))!.Addresses);

        // Something has to be the default, or the next order has nowhere to go.
        Assert.True(remaining.IsDefault);
        Assert.Equal("Office", remaining.Label);
    }

    // -----------------------------------------------------------------------
    // Blocking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Blocking_records_the_reason_and_unblocking_keeps_it_in_the_trail()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();
        var db = scope.ServiceProvider.GetRequiredService<EchoDbContext>();

        var created = await customers.CreateAsync(Request(UniquePhone()));

        var blocked = await customers.BlockAsync(created.Value, "Refused three COD parcels.");
        Assert.True(blocked.Succeeded, blocked.Error);

        var afterBlock = await customers.GetAsync(created.Value);
        Assert.True(afterBlock!.IsBlocked);
        Assert.Equal("Refused three COD parcels.", afterBlock.BlockReason);

        var unblocked = await customers.UnblockAsync(created.Value, "Spoke to her, sorted.");
        Assert.True(unblocked.Succeeded, unblocked.Error);

        var afterUnblock = await customers.GetAsync(created.Value);
        Assert.False(afterUnblock!.IsBlocked);
        Assert.Null(afterUnblock.BlockReason);

        // The column is cleared, so the audit trail is the only place the
        // original reason survives.
        var entry = await db.AuditLog
            .Where(a => a.Action == "Crm.Customer.Unblocked"
                        && a.EntityId == created.Value.ToString())
            .OrderByDescending(a => a.Id)
            .FirstAsync();

        Assert.Contains("Refused three COD parcels.", entry.DetailJson!);
    }

    [Fact]
    public async Task A_block_needs_a_reason()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var created = await customers.CreateAsync(Request(UniquePhone()));

        var blocked = await customers.BlockAsync(created.Value, "   ");

        Assert.False(blocked.Succeeded);
    }

    // -----------------------------------------------------------------------
    // Contact details
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Without_the_pii_permission_the_number_never_leaves_the_service()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var phone = UniquePhone();
        var created = await customers.CreateAsync(Request(phone));
        var districtId = await DistrictAsync(scope.ServiceProvider);

        await customers.SaveAddressAsync(new SaveAddressRequest
        {
            CustomerId = created.Value,
            DistrictId = districtId,
            AreaOrThana = "Mohammadpur",
            AddressLine = "House 19, Road 4",
            Landmark = "Opposite the mosque",
        });

        AsSalespersonWithoutPii();

        var detail = await customers.GetAsync(created.Value);

        Assert.NotNull(detail);
        Assert.DoesNotContain(phone, detail!.Phone, StringComparison.Ordinal);
        Assert.Contains("*", detail.Phone, StringComparison.Ordinal);

        var address = Assert.Single(detail.Addresses);

        // The street line is as identifying as the number; district and area
        // stay so somebody can still answer "where is this going?".
        Assert.DoesNotContain("House 19", address.AddressLine, StringComparison.Ordinal);
        Assert.Null(address.Landmark);
        Assert.Equal("Mohammadpur", address.AreaOrThana);
        Assert.Equal("Dhaka", address.DistrictName);
    }

    [Fact]
    public async Task The_grid_is_masked_too()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var phone = UniquePhone();
        var name = $"Masked {Guid.NewGuid().ToString("N")[..6]}";

        await customers.CreateAsync(Request(phone, name));

        AsSalespersonWithoutPii();

        var page = await customers.ListAsync(
            name, 0, 10, null, false, StatusFilter.Active, blockedOnly: false);

        var row = Assert.Single(page.Rows);

        Assert.DoesNotContain(phone, row.Phone, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_finds_a_customer_by_a_number_pasted_from_a_chat()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var phone = UniquePhone();
        await customers.CreateAsync(Request(phone, "Paste target"));

        // The form a number takes when it is copied out of Messenger.
        var page = await customers.ListAsync(
            $"+880 {phone[1..5]}-{phone[5..]}", 0, 10, null, false, StatusFilter.Active, false);

        Assert.Contains(page.Rows, r => r.FullName == "Paste target");
    }

    [Fact]
    public async Task Lookup_puts_an_exact_number_match_first()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var phone = UniquePhone();
        await customers.CreateAsync(Request(phone, "Exact match"));

        var matches = await customers.LookupAsync(phone);

        Assert.NotEmpty(matches);
        Assert.Equal("Exact match", matches[0].FullName);
    }

    [Fact]
    public async Task A_blocked_customer_is_flagged_in_the_lookup()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var phone = UniquePhone();
        var created = await customers.CreateAsync(Request(phone, "Blocked in lookup"));

        await customers.BlockAsync(created.Value, "Refused two parcels.");

        var match = (await customers.LookupAsync(phone)).First();

        // The order screen needs to know before it confirms, not after.
        Assert.True(match.IsBlocked);
        Assert.Equal("Refused two parcels.", match.BlockReason);
    }

    // -----------------------------------------------------------------------
    // Geography
    // -----------------------------------------------------------------------

    [Fact]
    public async Task All_eight_divisions_and_sixty_four_districts_are_seeded()
    {
        await using var scope = _fixture.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<CustomerAdminService>();

        var divisions = await customers.GetDivisionsAsync();
        var districts = await customers.GetDistrictsAsync();

        Assert.Equal(8, divisions.Count);
        Assert.Equal(64, districts.Count);

        // Every district belongs to a division that exists.
        var divisionIds = divisions.Select(d => d.Id).ToHashSet();
        Assert.All(districts, d => Assert.Contains(d.DivisionId, divisionIds));

        // Current official spellings, which is what courier APIs match on.
        Assert.Contains(districts, d => d.Name == "Chattogram");
        Assert.Contains(districts, d => d.Name == "Cumilla");
        Assert.DoesNotContain(districts, d => d.Name == "Chittagong");
    }

    private void AsSalespersonWithoutPii()
    {
        _fixture.CurrentUser.IsOwner = false;
        _fixture.CurrentUser.Grants.Clear();
        _fixture.CurrentUser.Grants.Add(Permissions.Crm.CustomerView);
    }
}
