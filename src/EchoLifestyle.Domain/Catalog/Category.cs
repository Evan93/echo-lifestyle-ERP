using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// A node in the merchandising tree (Skincare > Moisturisers > Night Creams).
///
/// The tree is stored with a materialised <see cref="Path"/> as well as a
/// parent pointer. The parent pointer is the truth; the path is a maintained
/// projection of it. Without the path, "every product under Skincare" - which
/// a storefront runs on nearly every page - is a recursive CTE per request.
/// With it, the same question is an indexed prefix match.
/// </summary>
public class Category : AuditableEntity, ISoftDeletable
{
    public long? ParentId { get; set; }

    public Category? Parent { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL segment, unique among non-deleted siblings of the same parent rather
    /// than globally: "cleansers" may legitimately exist under both Skincare
    /// and Haircare.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Ancestor ids from the root down to and including this node, delimited so
    /// that a prefix match cannot straddle an id boundary: "/1/7/22/".
    /// Rewritten whenever the node or any ancestor is re-parented. Never edited
    /// by hand.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Root is 0. Maintained alongside Path; used to cap the tree at the depth
    /// the navigation UI can actually render.
    /// </summary>
    public int Depth { get; set; }

    public string? Description { get; set; }

    public string? ImagePath { get; set; }

    /// <summary>Shown in the storefront's main navigation.</summary>
    public bool ShowInMenu { get; set; } = true;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public long? DeletedByUserId { get; set; }

    public ICollection<Category> Children { get; set; } = new List<Category>();

    public ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();

    /// <summary>The deepest level the tree is allowed to reach (root = 0).</summary>
    public const int MaxDepth = 2;

    /// <summary>
    /// Builds the path this node should have given its parent. Kept here rather
    /// than in a service so that the rule and the field it maintains live
    /// together.
    /// </summary>
    public static string BuildPath(Category? parent, long id) =>
        parent is null ? $"/{id}/" : $"{parent.Path}{id}/";
}
