using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Catalog;

/// <summary>
/// Products sit in many categories for browsing, but exactly one of those is
/// primary. The primary one drives breadcrumbs and category reporting, so that
/// "sales by category" cannot double-count a product filed in three places.
/// </summary>
public class ProductCategory : BaseEntity
{
    public long ProductId { get; set; }

    public Product? Product { get; set; }

    public long CategoryId { get; set; }

    public Category? Category { get; set; }

    public bool IsPrimary { get; set; }
}
