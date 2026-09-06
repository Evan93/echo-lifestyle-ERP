using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Administration;

/// <summary>
/// A selling location: the online channel today, physical stores later.
/// Branch is the unit of data scoping for non-owner users.
/// </summary>
public class Branch : AuditableEntity, ISoftDeletable
{
    public long CompanyId { get; set; }

    public Company? Company { get; set; }

    /// <summary>Short stable code used on documents and in reports, e.g. "ONLINE".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public BranchType Type { get; set; } = BranchType.Online;

    public string? AddressLine1 { get; set; }

    public string? City { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Negative stock is refused by default. Enabling it is a deliberate,
    /// per-branch policy decision and still requires the caller to hold
    /// Inventory.Stock.AllowNegative.
    /// </summary>
    public bool AllowNegativeStock { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }

    public ICollection<BranchWarehouse> BranchWarehouses { get; set; } = new List<BranchWarehouse>();
}

public enum BranchType
{
    Online = 1,
    Store = 2,
    Office = 3,
}
