namespace EchoLifestyle.Application.Catalog.Categories;

/// <summary>
/// One node, flattened for display. The tree is rendered from a flat ordered
/// list plus <see cref="Depth"/> rather than from nested objects: a nested
/// shape reads better in code and worse in a table, and the table is what the
/// admin screen is.
/// </summary>
public class CategoryNode
{
    public long Id { get; set; }

    public long? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int Depth { get; set; }

    public int DisplayOrder { get; set; }

    public bool ShowInMenu { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Products filed directly here, not counting descendants.</summary>
    public int DirectProductCount { get; set; }

    public int ChildCount { get; set; }
}

public class CategoryDetail
{
    public long Id { get; set; }

    public long? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? ImagePath { get; set; }

    public bool ShowInMenu { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public int Depth { get; set; }

    public int DirectProductCount { get; set; }

    public int ChildCount { get; set; }

    /// <summary>"Skincare > Moisturisers", for the form header.</summary>
    public string Breadcrumb { get; set; } = string.Empty;
}

public class SaveCategoryRequest
{
    public long? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public string? Description { get; set; }

    public string? ImagePath { get; set; }

    public bool ShowInMenu { get; set; } = true;

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>A category for a parent picker or a product's category checkboxes.</summary>
public class CategoryOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Depth { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Indented name for a plain HTML select, which cannot nest.</summary>
    public string IndentedName => Depth == 0 ? Name : new string(' ', Depth * 4) + "— " + Name;
}
