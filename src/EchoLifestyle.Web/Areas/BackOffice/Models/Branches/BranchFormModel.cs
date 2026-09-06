using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Administration.Branches;
using EchoLifestyle.Domain.Administration;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Branches;

/// <summary>
/// The branch form. Flat rather than wrapping the Application request object,
/// so bound field names stay short and the warehouse rows bind cleanly.
/// </summary>
public class BranchFormModel
{
    public long Id { get; set; }

    public bool IsNew => Id == 0;

    [Required(ErrorMessage = "Enter a branch code.")]
    [StringLength(20, MinimumLength = 2)]
    [Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a branch name.")]
    [StringLength(200)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Type")]
    public BranchType Type { get; set; } = BranchType.Online;

    [StringLength(250)]
    [Display(Name = "Address")]
    public string? AddressLine1 { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [StringLength(50)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Allow negative stock")]
    public bool AllowNegativeStock { get; set; }

    public List<WarehouseRow> Warehouses { get; set; } = [];

    public class WarehouseRow
    {
        public long WarehouseId { get; set; }

        public string WarehouseCode { get; set; } = string.Empty;

        public string WarehouseName { get; set; } = string.Empty;

        public bool IsLinked { get; set; }

        public bool IsPrimary { get; set; }

        [Range(1, 999)]
        public int Priority { get; set; } = 100;
    }

    public SaveBranchRequest ToRequest() => new()
    {
        Code = Code,
        Name = Name,
        Type = Type,
        AddressLine1 = AddressLine1,
        City = City,
        Phone = Phone,
        IsActive = IsActive,
        AllowNegativeStock = AllowNegativeStock,
        Warehouses = Warehouses
            .Select(w => new WarehouseAssignment
            {
                WarehouseId = w.WarehouseId,
                IsLinked = w.IsLinked,
                IsPrimary = w.IsLinked && w.IsPrimary,
                Priority = w.Priority,
            })
            .ToList(),
    };

    public static BranchFormModel FromDetail(BranchDetail detail) => new()
    {
        Id = detail.Id,
        Code = detail.Code,
        Name = detail.Name,
        Type = detail.Type,
        AddressLine1 = detail.AddressLine1,
        City = detail.City,
        Phone = detail.Phone,
        IsActive = detail.IsActive,
        AllowNegativeStock = detail.AllowNegativeStock,
        Warehouses = detail.Warehouses.Select(Row).ToList(),
    };

    public static BranchFormModel ForCreate(IReadOnlyList<BranchWarehouseLink> warehouses) => new()
    {
        Warehouses = warehouses.Select(Row).ToList(),
    };

    private static WarehouseRow Row(BranchWarehouseLink link) => new()
    {
        WarehouseId = link.WarehouseId,
        WarehouseCode = link.WarehouseCode,
        WarehouseName = link.WarehouseName,
        IsLinked = link.IsLinked,
        IsPrimary = link.IsPrimary,
        Priority = link.Priority,
    };
}
