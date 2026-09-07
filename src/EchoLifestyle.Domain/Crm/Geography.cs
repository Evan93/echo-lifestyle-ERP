using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Crm;

/// <summary>
/// One of the eight administrative divisions of Bangladesh.
///
/// Modelled as real tables rather than left as free text because every courier
/// in this market - Pathao, Steadfast, RedX, Sundarban - takes a division and a
/// district as codes, and delivery charges are set per district. Free-text
/// addresses would mean mapping "Chittagong", "Chattogram" and "ctg" by hand at
/// the moment an order ships, which is the worst possible time.
/// </summary>
public class Division : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Bangla name, for the storefront and printed labels.</summary>
    public string? NameBn { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<District> Districts { get; set; } = new List<District>();
}

/// <summary>
/// One of the 64 districts (zila). The level a courier prices by.
/// </summary>
public class District : BaseEntity
{
    public long DivisionId { get; set; }

    public Division? Division { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? NameBn { get; set; }

    /// <summary>
    /// The spelling this district had before the renames: Chittagong, Comilla,
    /// Bogra, Jessore.
    ///
    /// Stored on the row rather than kept in a lookup somewhere, because it is a
    /// fact about the district and three different things need it: the dropdown
    /// shows it so people can find the district by the name they know, search
    /// matches on it, and a courier or marketplace feed that still uses the old
    /// spelling can be mapped without a hand-written translation table.
    /// </summary>
    public string? FormerName { get; set; }

    /// <summary>
    /// True for the districts a same-day or next-day service covers. Dhaka
    /// only, for now - and the reason delivery charge cannot simply be a single
    /// company-wide number.
    /// </summary>
    public bool IsInsideCity { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
