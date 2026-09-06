using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Catalog.Categories;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Catalog;

public class CategoryFormModel
{
    public long Id { get; set; }

    public bool IsNew => Id == 0;

    [Display(Name = "Parent category")]
    public long? ParentId { get; set; }

    [Required(ErrorMessage = "Enter a category name.")]
    [StringLength(150)]
    [Display(Name = "Category name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(160)]
    [RegularExpression(
        "^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Use lowercase letters, numbers and single hyphens, for example 'night-creams'.")]
    [Display(Name = "Web address")]
    public string? Slug { get; set; }

    [StringLength(4000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [StringLength(400)]
    [Display(Name = "Image path")]
    public string? ImagePath { get; set; }

    [Display(Name = "Show in storefront menu")]
    public bool ShowInMenu { get; set; } = true;

    [Range(0, 9999)]
    [Display(Name = "Display order")]
    public int DisplayOrder { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public string Breadcrumb { get; set; } = string.Empty;

    public int DirectProductCount { get; set; }

    public int ChildCount { get; set; }

    /// <summary>Populated by the controller; excludes this node's own subtree.</summary>
    public IReadOnlyList<CategoryOption> ParentOptions { get; set; } = [];

    public SaveCategoryRequest ToRequest() => new()
    {
        ParentId = ParentId,
        Name = Name,
        Slug = Slug,
        Description = Description,
        ImagePath = ImagePath,
        ShowInMenu = ShowInMenu,
        DisplayOrder = DisplayOrder,
        IsActive = IsActive,
    };

    public static CategoryFormModel FromDetail(CategoryDetail detail) => new()
    {
        Id = detail.Id,
        ParentId = detail.ParentId,
        Name = detail.Name,
        Slug = detail.Slug,
        Description = detail.Description,
        ImagePath = detail.ImagePath,
        ShowInMenu = detail.ShowInMenu,
        DisplayOrder = detail.DisplayOrder,
        IsActive = detail.IsActive,
        Breadcrumb = detail.Breadcrumb,
        DirectProductCount = detail.DirectProductCount,
        ChildCount = detail.ChildCount,
    };
}
