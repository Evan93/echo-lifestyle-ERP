using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Infrastructure.Identity;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Users;

public class UserFormModel
{
    public long Id { get; set; }

    public bool IsNew => Id == 0;

    [Required(ErrorMessage = "Enter a username.")]
    [StringLength(64, MinimumLength = 3)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the person's full name.")]
    [StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    // Required because Identity is configured with RequireUniqueEmail; an
    // account without one cannot be created.
    [Required(ErrorMessage = "Enter an email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(250)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(500)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [DataType(DataType.Password)]
    [Display(Name = "Initial password")]
    public string? Password { get; set; }

    [Display(Name = "Roles")]
    public List<string> Roles { get; set; } = [];

    [Display(Name = "Branches")]
    public List<long> BranchIds { get; set; } = [];

    [Display(Name = "Default branch")]
    public long? DefaultBranchId { get; set; }

    // --- read-only context ---

    public bool IsLockedOut { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    public bool IsSelf { get; set; }

    public IReadOnlyList<string> AvailableRoles { get; set; } = [];

    public IReadOnlyList<BranchOption> AvailableBranches { get; set; } = [];

    public class BranchOption
    {
        public long Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public bool IsActive { get; init; }
    }

    public SaveStaffUserRequest ToRequest() => new()
    {
        UserName = UserName,
        FullName = FullName,
        Email = Email,
        Notes = Notes,
        IsActive = IsActive,
        Password = Password,
        Roles = Roles ?? [],
        BranchIds = BranchIds ?? [],
        DefaultBranchId = DefaultBranchId,
    };

    public static UserFormModel FromDetail(StaffUserDetail detail) => new()
    {
        Id = detail.Id,
        UserName = detail.UserName,
        FullName = detail.FullName,
        Email = detail.Email,
        Notes = detail.Notes,
        IsActive = detail.IsActive,
        IsLockedOut = detail.IsLockedOut,
        LastLoginAtUtc = detail.LastLoginAtUtc,
        Roles = [.. detail.Roles],
        BranchIds = [.. detail.BranchIds],
        DefaultBranchId = detail.DefaultBranchId,
    };
}

public class ResetPasswordModel
{
    public long Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the new password.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
