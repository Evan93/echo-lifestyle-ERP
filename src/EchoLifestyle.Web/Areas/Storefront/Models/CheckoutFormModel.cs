using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Storefront;

namespace EchoLifestyle.Web.Areas.Storefront.Models;

/// <summary>
/// The whole checkout: four fields and an address.
///
/// Nothing here carries a price, a product id or a total. The server reads the
/// basket from the cookie's token and prices it itself, so the only thing a
/// tampered form can change is where the parcel goes.
/// </summary>
public class CheckoutFormModel
{
    [Required(ErrorMessage = "Who is the parcel for?")]
    [StringLength(150)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "We need a mobile number to confirm the order.")]
    [StringLength(20)]
    [Display(Name = "Mobile number")]
    public string Phone { get; set; } = string.Empty;

    [Range(1, long.MaxValue, ErrorMessage = "Please choose your district.")]
    [Display(Name = "District")]
    public long DistrictId { get; set; }

    [Required(ErrorMessage = "Which area or thana?")]
    [StringLength(120)]
    [Display(Name = "Area or thana")]
    public string AreaOrThana { get; set; } = string.Empty;

    [Required(ErrorMessage = "We need a street address.")]
    [StringLength(300)]
    [Display(Name = "Address")]
    public string AddressLine { get; set; } = string.Empty;

    [StringLength(150)]
    [Display(Name = "Landmark")]
    public string? Landmark { get; set; }

    [StringLength(300)]
    [Display(Name = "Anything we should know")]
    public string? DeliveryNotes { get; set; }

    public ShopCart Cart { get; set; } = new();

    public IReadOnlyList<DistrictOption> Districts { get; set; } = [];

    public DeliveryQuote Delivery { get; set; } = new();

    public PlaceOrderRequest ToRequest() => new()
    {
        FullName = FullName,
        Phone = Phone,
        DistrictId = DistrictId,
        AreaOrThana = AreaOrThana,
        AddressLine = AddressLine,
        Landmark = Landmark,
        DeliveryNotes = DeliveryNotes,
    };
}

public class DistrictOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Chittagong, Comilla, Bogra, Jessore - what people still type.</summary>
    public string? FormerName { get; set; }

    public string DivisionName { get; set; } = string.Empty;

    public string Display => string.IsNullOrWhiteSpace(FormerName)
        ? Name
        : $"{Name} ({FormerName})";
}
