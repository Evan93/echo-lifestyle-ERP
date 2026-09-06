using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Administration;

/// <summary>
/// Many-to-many link between branches and the warehouses they can draw stock from.
/// Exactly one warehouse per branch is marked primary; that is the default
/// fulfilment source when a document does not name one explicitly.
/// </summary>
public class BranchWarehouse : AuditableEntity
{
    public long BranchId { get; set; }

    public Branch? Branch { get; set; }

    public long WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    /// <summary>Default fulfilment source for this branch.</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Lower numbers are picked first when sourcing stock.</summary>
    public int Priority { get; set; } = 100;
}
