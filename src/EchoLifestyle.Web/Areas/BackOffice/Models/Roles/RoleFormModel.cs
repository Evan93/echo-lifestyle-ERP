using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Infrastructure.Identity;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Roles;

public class RoleFormModel
{
    public long Id { get; set; }

    public bool IsNew => Id == 0;

    [Required(ErrorMessage = "Enter a role name.")]
    [StringLength(100)]
    [Display(Name = "Role name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    public List<string> Permissions { get; set; } = [];

    // --- read-only context ---

    public bool IsSystemRole { get; set; }

    /// <summary>True for Owner, which always holds every permission.</summary>
    public bool PermissionsAreFixed { get; set; }

    public int UserCount { get; set; }

    public SaveRoleRequest ToRequest() => new()
    {
        Name = Name,
        Description = Description,
        Permissions = Permissions ?? [],
    };

    public static RoleFormModel FromDetail(RoleDetail detail) => new()
    {
        Id = detail.Id,
        Name = detail.Name,
        Description = detail.Description,
        IsSystemRole = detail.IsSystemRole,
        PermissionsAreFixed = detail.PermissionsAreFixed,
        UserCount = detail.UserCount,
        Permissions = [.. detail.Permissions],
    };
}
