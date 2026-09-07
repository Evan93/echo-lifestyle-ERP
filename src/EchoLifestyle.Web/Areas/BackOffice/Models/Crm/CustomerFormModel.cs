using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Crm.Customers;
using EchoLifestyle.Domain.Crm;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Crm;

public class CustomerFormModel
{
    public long Id { get; set; }

    [StringLength(20)]
    [Display(Name = "Code")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Enter a name.")]
    [StringLength(200)]
    [Display(Name = "Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a mobile number - it is how this customer is identified.")]
    [StringLength(20)]
    [Display(Name = "Mobile")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(20)]
    [Display(Name = "Other number")]
    public string? AlternatePhone { get; set; }

    [StringLength(200)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Display(Name = "Type")]
    public CustomerType CustomerType { get; set; } = CustomerType.Retail;

    [Display(Name = "Price list")]
    public long? PriceListId { get; set; }

    [Display(Name = "Came from")]
    public CustomerSource Source { get; set; } = CustomerSource.Unknown;

    [Display(Name = "Date of birth")]
    [DataType(DataType.Date)]
    public DateOnly? DateOfBirth { get; set; }

    [StringLength(2000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<PriceListOption> PriceLists { get; set; } = [];

    public bool IsEdit => Id > 0;

    public SaveCustomerRequest ToRequest() => new()
    {
        Code = Code,
        FullName = FullName,
        Phone = Phone,
        AlternatePhone = AlternatePhone,
        Email = Email,
        CustomerType = CustomerType,
        PriceListId = PriceListId,
        Source = Source,
        DateOfBirth = DateOfBirth,
        Notes = Notes,
        IsActive = IsActive,
    };

    public static CustomerFormModel From(CustomerDetail detail) => new()
    {
        Id = detail.Id,
        Code = detail.Code,
        FullName = detail.FullName,
        Phone = detail.Phone,
        AlternatePhone = detail.AlternatePhone,
        Email = detail.Email,
        CustomerType = detail.CustomerType,
        PriceListId = detail.PriceListId,
        Source = detail.Source,
        DateOfBirth = detail.DateOfBirth,
        Notes = detail.Notes,
        IsActive = detail.IsActive,
    };
}

public class PriceListOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// The address sub-form. Posted on its own rather than with the customer,
/// because one customer has several and a single form that saves all of them
/// saves the wrong one.
/// </summary>
public class CustomerAddressFormModel
{
    public long? Id { get; set; }

    public long CustomerId { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "Label")]
    public string Label { get; set; } = "Home";

    [StringLength(200)]
    [Display(Name = "Deliver to")]
    public string? RecipientName { get; set; }

    [StringLength(20)]
    [Display(Name = "Their number")]
    public string? RecipientPhone { get; set; }

    [Display(Name = "Division")]
    public long? DivisionId { get; set; }

    [Required(ErrorMessage = "Choose a district.")]
    [Display(Name = "District")]
    public long DistrictId { get; set; }

    [Required(ErrorMessage = "Enter the area or thana.")]
    [StringLength(150)]
    [Display(Name = "Area / thana")]
    public string AreaOrThana { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the address.")]
    [StringLength(500)]
    [Display(Name = "House, road, flat")]
    public string AddressLine { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Landmark")]
    public string? Landmark { get; set; }

    [StringLength(10)]
    [Display(Name = "Post code")]
    public string? PostCode { get; set; }

    [StringLength(500)]
    [Display(Name = "Notes for the courier")]
    public string? DeliveryNotes { get; set; }

    [Display(Name = "Default address")]
    public bool IsDefault { get; set; }

    public SaveAddressRequest ToRequest() => new()
    {
        Id = Id,
        CustomerId = CustomerId,
        Label = Label,
        RecipientName = RecipientName,
        RecipientPhone = RecipientPhone,
        DistrictId = DistrictId,
        AreaOrThana = AreaOrThana,
        AddressLine = AddressLine,
        Landmark = Landmark,
        PostCode = PostCode,
        DeliveryNotes = DeliveryNotes,
        IsDefault = IsDefault,
    };
}

/// <summary>Blocking a customer. A reason is not optional.</summary>
public class BlockCustomerModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Say why they are being blocked.")]
    [StringLength(500, MinimumLength = 3)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;
}
