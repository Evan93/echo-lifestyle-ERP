using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Crm;

/// <summary>
/// Somebody the business sells to.
///
/// <see cref="Phone"/> is the identity, not the name. Orders arrive by
/// Messenger, Instagram and phone; the same person appears as "Rima", "Rima
/// Akter" and "রিমা" across three conversations, and the only thing that stays
/// constant is the number they order from. It is stored normalised and unique,
/// so a second order from the same number attaches to the same customer instead
/// of quietly creating a stranger with no history.
/// </summary>
public class Customer : AuditableEntity, ISoftDeletable
{
    /// <summary>CUS-00001. Printed on invoices and quoted in messages.</summary>
    public string Code { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Normalised to 01XXXXXXXXX by <c>BangladeshPhone</c> before it is stored.
    /// Unique across non-deleted customers.
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// A second number - a husband's, an office line, the number the courier
    /// should try when the first one is off. Not unique: two family members
    /// legitimately share a fallback.
    /// </summary>
    public string? AlternatePhone { get; set; }

    /// <summary>
    /// Optional, and genuinely so. A large share of customers here have no
    /// email they use, and demanding one would mean typing a fake.
    /// </summary>
    public string? Email { get; set; }

    public CustomerType CustomerType { get; set; } = CustomerType.Retail;

    /// <summary>
    /// Overrides the default selling prices for this customer. Null means the
    /// standard retail list - which is every customer until wholesale starts.
    /// </summary>
    public long? PriceListId { get; set; }

    public PriceList? PriceList { get; set; }

    /// <summary>Where they came from. The only marketing attribution that survives contact with reality.</summary>
    public CustomerSource Source { get; set; } = CustomerSource.Unknown;

    public DateOnly? DateOfBirth { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Refuse further orders from this customer.
    ///
    /// Cash on delivery makes this necessary rather than punitive: a customer
    /// who has refused three deliveries has cost the business the courier fee
    /// three times, and the person taking the next order needs to know before
    /// they confirm it, not after.
    /// </summary>
    public bool IsBlocked { get; set; }

    public string? BlockReason { get; set; }

    public DateTime? BlockedAtUtc { get; set; }

    public long? BlockedByUserId { get; set; }

    /// <summary>
    /// The storefront account, once they make one.
    ///
    /// A plain id rather than a navigation: users live in the Identity model and
    /// the domain does not depend on it. Null for everybody who has only ever
    /// ordered through a message, which is how checkout will work - guest
    /// first, account afterwards if they want one.
    /// </summary>
    public long? UserId { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }

    public ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();
}

public enum CustomerType
{
    Retail = 1,

    /// <summary>Buys to resell. Gets a price list of their own.</summary>
    Wholesale = 2,
}

/// <summary>
/// How the customer reached the business. Ordered roughly by how the orders
/// actually arrive today.
/// </summary>
public enum CustomerSource
{
    Unknown = 0,
    Facebook = 1,
    Messenger = 2,
    Instagram = 3,
    WhatsApp = 4,
    Phone = 5,
    Website = 6,
    Referral = 7,
    WalkIn = 8,
    Other = 99,
}

/// <summary>
/// Somewhere to deliver to.
///
/// Not the same shape as a Western address. There is no reliable street
/// numbering outside a few Dhaka neighbourhoods, post codes are rarely used, and
/// a courier finds the house from <see cref="Landmark"/> more often than from
/// anything else on this record - which is why it is a first-class field rather
/// than a line squeezed into the address.
/// </summary>
public class CustomerAddress : AuditableEntity
{
    public long CustomerId { get; set; }

    public Customer? Customer { get; set; }

    /// <summary>"Home", "Office", "Ma's place".</summary>
    public string Label { get; set; } = "Home";

    /// <summary>
    /// Who receives it. Often not the customer: gifts and family orders are
    /// common, and the courier needs the name at the door, not the one on the
    /// account.
    /// </summary>
    public string RecipientName { get; set; } = string.Empty;

    /// <summary>Normalised. The number the courier rings on arrival.</summary>
    public string RecipientPhone { get; set; } = string.Empty;

    public long DivisionId { get; set; }

    public Division? Division { get; set; }

    public long DistrictId { get; set; }

    public District? District { get; set; }

    /// <summary>Thana, upazila or the local name for the area. Free text - the list below district is long and unstable.</summary>
    public string AreaOrThana { get; set; } = string.Empty;

    /// <summary>House, road, block, flat.</summary>
    public string AddressLine { get; set; } = string.Empty;

    /// <summary>"Beside the Aarong showroom." Often the only part that finds the door.</summary>
    public string? Landmark { get; set; }

    public string? PostCode { get; set; }

    /// <summary>"Call before 6, gate closes after that." Passed to the courier.</summary>
    public string? DeliveryNotes { get; set; }

    /// <summary>At most one per customer, enforced by a filtered unique index.</summary>
    public bool IsDefault { get; set; }
}
