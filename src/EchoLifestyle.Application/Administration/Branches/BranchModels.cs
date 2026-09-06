using EchoLifestyle.Domain.Administration;

namespace EchoLifestyle.Application.Administration.Branches;

/// <summary>
/// Which branches a list should include.
///
/// An explicit three-way filter rather than a "show inactive" flag: a checkbox
/// left unticked hides rows silently, so a row disappearing after an edit reads
/// as a bug rather than as a filter doing its job.
/// </summary>
public enum BranchStatusFilter
{
    Active = 0,
    Inactive = 1,
    All = 2,
}

/// <summary>One row in the branch list.</summary>
public class BranchListItem
{
    public long Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public BranchType Type { get; init; }

    public string? City { get; init; }

    public bool IsActive { get; init; }

    public bool AllowNegativeStock { get; init; }

    public int WarehouseCount { get; init; }

    public string? PrimaryWarehouseName { get; init; }
}

/// <summary>A branch as the edit form sees it.</summary>
public class BranchDetail
{
    public long Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public BranchType Type { get; init; }

    public string? AddressLine1 { get; init; }

    public string? City { get; init; }

    public string? Phone { get; init; }

    public bool IsActive { get; init; }

    public bool AllowNegativeStock { get; init; }

    public IReadOnlyList<BranchWarehouseLink> Warehouses { get; init; } = [];
}

public class BranchWarehouseLink
{
    public long WarehouseId { get; init; }

    public string WarehouseCode { get; init; } = string.Empty;

    public string WarehouseName { get; init; } = string.Empty;

    public bool IsLinked { get; init; }

    public bool IsPrimary { get; init; }

    public int Priority { get; init; } = 100;
}

/// <summary>What the form sends back when saving.</summary>
public class SaveBranchRequest
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public BranchType Type { get; set; } = BranchType.Online;

    public string? AddressLine1 { get; set; }

    public string? City { get; set; }

    public string? Phone { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Turning this on lets documents drive stock below zero at this branch.
    /// It is audited separately from other edits because it weakens an
    /// inventory guarantee rather than just changing a detail.
    /// </summary>
    public bool AllowNegativeStock { get; set; }

    public List<WarehouseAssignment> Warehouses { get; set; } = [];
}

public class WarehouseAssignment
{
    public long WarehouseId { get; set; }

    public bool IsLinked { get; set; }

    public bool IsPrimary { get; set; }

    public int Priority { get; set; } = 100;
}
