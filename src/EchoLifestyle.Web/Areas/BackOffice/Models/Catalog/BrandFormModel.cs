using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Catalog.Brands;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Catalog;

public class BrandFormModel
{
    public long Id { get; set; }

    public bool IsNew => Id == 0;

    [Required(ErrorMessage = "Enter a brand name.")]
    [StringLength(150)]
    [Display(Name = "Brand name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Not required: left blank the service derives one from the name. It is
    /// only shown as an editable field so that an established brand page can
    /// keep its address when the display name is corrected.
    /// </summary>
    [StringLength(160)]
    [RegularExpression(
        "^[a-z0-9]+(-[a-z0-9]+)*$",
        ErrorMessage = "Use lowercase letters, numbers and single hyphens, for example 'the-ordinary'.")]
    [Display(Name = "Web address")]
    public string? Slug { get; set; }

    [StringLength(4000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [StringLength(100)]
    [Display(Name = "Country of origin")]
    public string? OriginCountry { get; set; }

    [StringLength(400)]
    [Display(Name = "Logo path")]
    public string? LogoPath { get; set; }

    [StringLength(400)]
    [Display(Name = "Banner path")]
    public string? BannerPath { get; set; }

    [Display(Name = "Feature on the home page")]
    public bool IsFeatured { get; set; }

    [Range(0, 9999)]
    [Display(Name = "Display order")]
    public int DisplayOrder { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Read-only: drives whether Delete is offered.</summary>
    public int ProductCount { get; set; }

    public SaveBrandRequest ToRequest() => new()
    {
        Name = Name,
        Slug = Slug,
        Description = Description,
        OriginCountry = OriginCountry,
        LogoPath = LogoPath,
        BannerPath = BannerPath,
        IsFeatured = IsFeatured,
        DisplayOrder = DisplayOrder,
        IsActive = IsActive,
    };

    public static BrandFormModel FromDetail(BrandDetail detail) => new()
    {
        Id = detail.Id,
        Name = detail.Name,
        Slug = detail.Slug,
        Description = detail.Description,
        OriginCountry = detail.OriginCountry,
        LogoPath = detail.LogoPath,
        BannerPath = detail.BannerPath,
        IsFeatured = detail.IsFeatured,
        DisplayOrder = detail.DisplayOrder,
        IsActive = detail.IsActive,
        ProductCount = detail.ProductCount,
    };
}
