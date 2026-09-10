using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Marketing;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Marketing;

public class BannerFormModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Give the banner a name so you can find it later.")]
    [StringLength(120)]
    [Display(Name = "Name (internal)")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Headline")]
    public string? Headline { get; set; }

    [StringLength(400)]
    [Display(Name = "Sub-heading")]
    public string? Subheading { get; set; }

    [Required(ErrorMessage = "Describe the image for people who cannot see it.")]
    [StringLength(300)]
    [Display(Name = "Image description")]
    public string AltText { get; set; } = string.Empty;

    /// <summary>
    /// Checked here as well as in the service, for different reasons. The
    /// service drops anything that is not a real destination, because this
    /// value becomes an href on the home page and silently storing nothing is
    /// the safe failure. But silently storing nothing is also a confusing one -
    /// somebody types "123", saves, and finds the field empty afterwards with
    /// no explanation. This says why before it gets that far.
    /// </summary>
    [RegularExpression(
        @"^(?:/(?!/)\S*|https?://\S+)$",
        ErrorMessage = "Use a path on this site like /c/skincare, or a full https:// address.")]
    [StringLength(500)]
    [Display(Name = "Link")]
    public string? LinkUrl { get; set; }

    [StringLength(60)]
    [Display(Name = "Button text")]
    public string? ButtonText { get; set; }

    [Display(Name = "Order")]
    public int DisplayOrder { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Typed as local dates because that is what somebody scheduling an Eid
    /// campaign means. Converted at the boundary; everything stored is UTC.
    /// </summary>
    [Display(Name = "Show from")]
    [DataType(DataType.DateTime)]
    public DateTime? StartsAt { get; set; }

    [Display(Name = "Show until")]
    [DataType(DataType.DateTime)]
    public DateTime? EndsAt { get; set; }

    /// <summary>Set on edit, so the form can show what is already there.</summary>
    public string? ExistingImagePath { get; set; }

    public string? ExistingMobileImagePath { get; set; }

    public SaveBannerRequest ToRequest() => new()
    {
        Name = Name,
        Headline = Headline,
        Subheading = Subheading,
        AltText = AltText,
        LinkUrl = LinkUrl,
        ButtonText = ButtonText,
        DisplayOrder = DisplayOrder,
        IsActive = IsActive,

        // DateTimeKind.Unspecified comes out of the model binder, so it is
        // stated rather than assumed. Everything below the web layer is UTC.
        StartsAtUtc = StartsAt is { } from
            ? DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime()
            : null,
        EndsAtUtc = EndsAt is { } to
            ? DateTime.SpecifyKind(to, DateTimeKind.Local).ToUniversalTime()
            : null,
    };

    public static BannerFormModel FromDetail(BannerDetail detail) => new()
    {
        Id = detail.Id,
        Name = detail.Name,
        Headline = detail.Headline,
        Subheading = detail.Subheading,
        AltText = detail.AltText,
        LinkUrl = detail.LinkUrl,
        ButtonText = detail.ButtonText,
        DisplayOrder = detail.DisplayOrder,
        IsActive = detail.IsActive,
        StartsAt = detail.StartsAtUtc?.ToLocalTime(),
        EndsAt = detail.EndsAtUtc?.ToLocalTime(),
        ExistingImagePath = detail.ImagePath,
        ExistingMobileImagePath = detail.MobileImagePath,
    };
}
