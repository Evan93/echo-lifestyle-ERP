using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Crm;

namespace EchoLifestyle.Application.Crm.Customers;

public class SaveCustomerRequest
{
    /// <summary>Blank generates the next CUS-00001.</summary>
    public string? Code { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Typed however the person likes; normalised before it is stored.</summary>
    public string Phone { get; set; } = string.Empty;

    public string? AlternatePhone { get; set; }

    public string? Email { get; set; }

    public CustomerType CustomerType { get; set; } = CustomerType.Retail;

    public long? PriceListId { get; set; }

    public CustomerSource Source { get; set; } = CustomerSource.Unknown;

    public DateOnly? DateOfBirth { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// The fast path: a name and a number, typed while somebody is on the phone.
///
/// Everything else can be filled in later, or never. A create form that demands
/// an email and an address is a form people work around by not using it, and the
/// customer ends up recorded nowhere.
/// </summary>
public class QuickCreateCustomerRequest
{
    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public CustomerSource Source { get; set; } = CustomerSource.Unknown;
}

public class SaveAddressRequest
{
    public long? Id { get; set; }

    public long CustomerId { get; set; }

    public string Label { get; set; } = "Home";

    /// <summary>Blank falls back to the customer's own name.</summary>
    public string? RecipientName { get; set; }

    /// <summary>Blank falls back to the customer's own number.</summary>
    public string? RecipientPhone { get; set; }

    public long DistrictId { get; set; }

    public string AreaOrThana { get; set; } = string.Empty;

    public string AddressLine { get; set; } = string.Empty;

    public string? Landmark { get; set; }

    public string? PostCode { get; set; }

    public string? DeliveryNotes { get; set; }

    public bool IsDefault { get; set; }
}

public class CustomerListItem
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Already masked or not, depending on who asked. See <c>CustomerAdminService</c>.</summary>
    public string Phone { get; set; } = string.Empty;

    public string? Email { get; set; }

    public CustomerType CustomerType { get; set; }

    public CustomerSource Source { get; set; }

    public string? DistrictName { get; set; }

    public int AddressCount { get; set; }

    public bool IsBlocked { get; set; }

    public string? BlockReason { get; set; }

    public bool HasAccount { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public class CustomerDetail
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string? AlternatePhone { get; set; }

    public string? Email { get; set; }

    public CustomerType CustomerType { get; set; }

    public long? PriceListId { get; set; }

    public string? PriceListName { get; set; }

    public CustomerSource Source { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Notes { get; set; }

    public bool IsBlocked { get; set; }

    public string? BlockReason { get; set; }

    public DateTime? BlockedAtUtc { get; set; }

    public bool HasAccount { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public IReadOnlyList<CustomerAddressItem> Addresses { get; set; } = [];

    /// <summary>Formatted for reading aloud, when the viewer is allowed to see it.</summary>
    public string PhoneDisplay => Phone.Length == 11 ? BangladeshPhone.Format(Phone) : Phone;
}

public class CustomerAddressItem
{
    public long Id { get; set; }

    public string Label { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public string RecipientPhone { get; set; } = string.Empty;

    public long DivisionId { get; set; }

    public string DivisionName { get; set; } = string.Empty;

    public long DistrictId { get; set; }

    public string DistrictName { get; set; } = string.Empty;

    public bool IsInsideCity { get; set; }

    public string AreaOrThana { get; set; } = string.Empty;

    public string AddressLine { get; set; } = string.Empty;

    public string? Landmark { get; set; }

    public string? PostCode { get; set; }

    public string? DeliveryNotes { get; set; }

    public bool IsDefault { get; set; }

    /// <summary>
    /// The address on one line, in the order a courier reads it: specific
    /// first, general last.
    /// </summary>
    public string OneLine
    {
        get
        {
            var parts = new List<string> { AddressLine, AreaOrThana, DistrictName };

            if (!string.IsNullOrWhiteSpace(Landmark))
            {
                parts.Insert(1, $"({Landmark})");
            }

            return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }
}

/// <summary>
/// A customer match while somebody is typing an order. Deliberately thin - this
/// is a dropdown, not a screen.
/// </summary>
public class CustomerLookupItem
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public bool IsBlocked { get; set; }

    public string? BlockReason { get; set; }

    public string? DefaultAddress { get; set; }

    /// <summary>Ordering only - an exact phone match beats a name that contains the term.</summary>
    public int Rank { get; set; }
}

public class DivisionOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class DistrictOption
{
    public long Id { get; set; }

    public long DivisionId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The pre-rename spelling, where there is one.</summary>
    public string? FormerName { get; set; }

    public bool IsInsideCity { get; set; }

    /// <summary>
    /// "Chattogram (Chittagong)". Browsers type-ahead on the visible text of an
    /// option, so putting the old name here is what lets somebody find the
    /// district by the name they actually know.
    /// </summary>
    public string Display => FormerName is null ? Name : $"{Name} ({FormerName})";
}
