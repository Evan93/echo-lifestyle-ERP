using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Administration.Warehouses;
using EchoLifestyle.Domain.Administration;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Warehouses;

public class WarehouseFormModel
{
    public long Id { get; set; }

    public bool IsNew => Id == 0;

    [Required(ErrorMessage = "Enter a warehouse code.")]
    [StringLength(20, MinimumLength = 2)]
    [Display(Name = "Code")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a warehouse name.")]
    [StringLength(200)]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Kind")]
    public WarehouseKind Kind { get; set; } = WarehouseKind.Sellable;

    [StringLength(250)]
    [Display(Name = "Address")]
    public string? AddressLine1 { get; set; }

    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Shown read-only: links are edited from the branch form.</summary>
    public IReadOnlyList<string> LinkedBranches { get; set; } = [];

    public IReadOnlyList<string> PrimaryForBranches { get; set; } = [];

    public SaveWarehouseRequest ToRequest() => new()
    {
        Code = Code,
        Name = Name,
        Kind = Kind,
        AddressLine1 = AddressLine1,
        City = City,
        IsActive = IsActive,
    };

    public static WarehouseFormModel FromDetail(WarehouseDetail detail) => new()
    {
        Id = detail.Id,
        Code = detail.Code,
        Name = detail.Name,
        Kind = detail.Kind,
        AddressLine1 = detail.AddressLine1,
        City = detail.City,
        IsActive = detail.IsActive,
        LinkedBranches = detail.LinkedBranches,
        PrimaryForBranches = detail.PrimaryForBranches,
    };
}
