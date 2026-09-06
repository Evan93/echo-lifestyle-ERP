using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Administration;

/// <summary>
/// A physical stock-holding location. Warehouses are deliberately not owned
/// by a single branch: several branches may draw from one warehouse, and one
/// branch may draw from several. See BranchWarehouse.
/// </summary>
public class Warehouse : AuditableEntity, ISoftDeletable
{
    public long CompanyId { get; set; }

    public Company? Company { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? AddressLine1 { get; set; }

    public string? City { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Quarantine/damage holding locations are excluded from available stock.
    /// </summary>
    public WarehouseKind Kind { get; set; } = WarehouseKind.Sellable;

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }

    public ICollection<BranchWarehouse> BranchWarehouses { get; set; } = new List<BranchWarehouse>();
}

public enum WarehouseKind
{
    /// <summary>Normal stock available to sell.</summary>
    Sellable = 1,

    /// <summary>Received but not yet cleared for sale.</summary>
    Quarantine = 2,

    /// <summary>Damaged or expired stock held pending write-off.</summary>
    Damaged = 3,

    /// <summary>Stock in transit between locations.</summary>
    Transit = 4,
}
