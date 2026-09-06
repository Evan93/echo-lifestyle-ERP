using EchoLifestyle.Domain.Administration;

namespace EchoLifestyle.Application.Administration.Warehouses;

public class WarehouseListItem
{
    public long Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public WarehouseKind Kind { get; init; }

    public string? City { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Branches that can draw from this warehouse.</summary>
    public int BranchCount { get; init; }

    /// <summary>Branches for which this is the default fulfilment source.</summary>
    public int PrimaryForBranchCount { get; init; }
}

public class WarehouseDetail
{
    public long Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public WarehouseKind Kind { get; init; }

    public string? AddressLine1 { get; init; }

    public string? City { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Read-only on this screen: branches are linked from the branch form.</summary>
    public IReadOnlyList<string> LinkedBranches { get; init; } = [];

    public IReadOnlyList<string> PrimaryForBranches { get; init; } = [];
}

public class SaveWarehouseRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Sellable stock counts toward availability; quarantine, damaged and
    /// transit locations do not. Changing this changes what the business can
    /// sell, so it is audited as its own action.
    /// </summary>
    public WarehouseKind Kind { get; set; } = WarehouseKind.Sellable;

    public string? AddressLine1 { get; set; }

    public string? City { get; set; }

    public bool IsActive { get; set; } = true;
}
