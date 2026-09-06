using System.Globalization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Catalog.Categories;

/// <summary>
/// The merchandising tree.
///
/// The parent pointer is the truth and <see cref="Category.Path"/> is a
/// maintained projection of it. Every write that can change an ancestor chain
/// goes through <see cref="RewriteSubtreeAsync"/>, so there is exactly one
/// place where paths are produced and exactly one place to be wrong.
/// </summary>
public class CategoryAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public CategoryAdminService(IApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// The whole tree in display order, flattened.
    ///
    /// Loaded in one query and ordered in memory: the tree is a few dozen rows
    /// at most, and depth-first display order is not something SQL expresses
    /// cheaply. Paging a tree would be meaningless, so there is none.
    /// </summary>
    public async Task<IReadOnlyList<CategoryNode>> GetTreeAsync(
        StatusFilter status = StatusFilter.All,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Categories.AsNoTracking();

        var all = await query
            .Select(c => new CategoryNode
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                Slug = c.Slug,
                Path = c.Path,
                Depth = c.Depth,
                DisplayOrder = c.DisplayOrder,
                ShowInMenu = c.ShowInMenu,
                IsActive = c.IsActive,
                DirectProductCount = c.ProductCategories.Count,
                ChildCount = c.Children.Count(x => !x.IsDeleted),
            })
            .ToListAsync(cancellationToken);

        var ordered = OrderDepthFirst(all);

        if (status != StatusFilter.All)
        {
            var wantActive = status == StatusFilter.Active;

            // Filtering a tree by status hides branches, not just leaves: a node
            // whose parent is hidden has nowhere to be drawn, so it goes too.
            var visible = ordered.Where(n => n.IsActive == wantActive).Select(n => n.Id).ToHashSet();

            ordered = ordered
                .Where(n => visible.Contains(n.Id)
                            && (n.ParentId is null || visible.Contains(n.ParentId.Value)))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            // A match keeps its ancestors so the result still reads as a tree
            // rather than as a list of orphans.
            var matchIds = ordered
                .Where(n => n.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(n => n.Path)
                .ToList();

            ordered = ordered
                .Where(n => matchIds.Any(p => p.StartsWith(n.Path, StringComparison.Ordinal)
                                              || n.Path.StartsWith(p, StringComparison.Ordinal)))
                .ToList();
        }

        return ordered;
    }

    public async Task<CategoryDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategoryDetail
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                Slug = c.Slug,
                Description = c.Description,
                ImagePath = c.ImagePath,
                ShowInMenu = c.ShowInMenu,
                DisplayOrder = c.DisplayOrder,
                IsActive = c.IsActive,
                Depth = c.Depth,
                DirectProductCount = c.ProductCategories.Count,
                ChildCount = c.Children.Count(x => !x.IsDeleted),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (category is null)
        {
            return null;
        }

        category.Breadcrumb = await BuildBreadcrumbAsync(category.Id, cancellationToken);

        return category;
    }

    /// <summary>
    /// Categories that may be chosen as a parent.
    ///
    /// Excludes the node being edited and everything beneath it - a node cannot
    /// become its own ancestor - and excludes anything already at the maximum
    /// depth, which could not hold a child.
    /// </summary>
    public async Task<IReadOnlyList<CategoryOption>> GetParentOptionsAsync(
        long? excludingSubtreeOf = null,
        CancellationToken cancellationToken = default)
    {
        string? excludedPath = null;

        if (excludingSubtreeOf is not null)
        {
            excludedPath = await _db.Categories
                .Where(c => c.Id == excludingSubtreeOf)
                .Select(c => c.Path)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var candidates = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Depth < Category.MaxDepth)
            .Select(c => new { c.Id, c.Name, c.Depth, c.Path, c.IsActive, c.ParentId, c.DisplayOrder })
            .ToListAsync(cancellationToken);

        var filtered = excludedPath is null
            ? candidates
            : candidates.Where(c => !c.Path.StartsWith(excludedPath, StringComparison.Ordinal)).ToList();

        var nodes = filtered
            .Select(c => new CategoryNode
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                Path = c.Path,
                Depth = c.Depth,
                DisplayOrder = c.DisplayOrder,
                IsActive = c.IsActive,
            })
            .ToList();

        return OrderDepthFirst(nodes)
            .Select(n => new CategoryOption
            {
                Id = n.Id,
                Name = n.Name,
                Depth = n.Depth,
                IsActive = n.IsActive,
            })
            .ToList();
    }

    /// <summary>Every category, for a product's category picker.</summary>
    public async Task<IReadOnlyList<CategoryOption>> GetAllOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await _db.Categories
            .AsNoTracking()
            .Select(c => new CategoryNode
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                Path = c.Path,
                Depth = c.Depth,
                DisplayOrder = c.DisplayOrder,
                IsActive = c.IsActive,
            })
            .ToListAsync(cancellationToken);

        return OrderDepthFirst(all)
            .Select(n => new CategoryOption
            {
                Id = n.Id,
                Name = n.Name,
                Depth = n.Depth,
                IsActive = n.IsActive,
            })
            .ToList();
    }

    public async Task<OperationResult<long>> CreateAsync(
        SaveCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var parent = await LoadParentAsync(request.ParentId, cancellationToken);

        if (request.ParentId is not null && parent is null)
        {
            return OperationResult<long>.Failure(
                "That parent category no longer exists.", nameof(SaveCategoryRequest.ParentId));
        }

        var depth = parent is null ? 0 : parent.Depth + 1;

        if (depth > Category.MaxDepth)
        {
            return OperationResult<long>.Failure(
                $"The tree is limited to {Category.MaxDepth + 1} levels - a deeper menu stops being usable. "
                + "File this under a higher category instead.",
                nameof(SaveCategoryRequest.ParentId));
        }

        var validation = ValidateFields(request);
        if (!validation.Succeeded)
        {
            return OperationResult<long>.Failure(validation.Error!, validation.Field);
        }

        var slug = await ResolveSlugAsync(request, request.ParentId, existingId: null, cancellationToken);
        if (!slug.Succeeded)
        {
            return OperationResult<long>.Failure(slug.Error!, slug.Field);
        }

        var category = new Category
        {
            ParentId = request.ParentId,
            Name = request.Name.Trim(),
            Slug = slug.Value!,
            Description = Trim(request.Description),
            ImagePath = Trim(request.ImagePath),
            ShowInMenu = request.ShowInMenu,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
            Depth = depth,

            // Placeholder: the real path needs the identity value, which only
            // exists after the insert.
            Path = "/",
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);

        category.Path = Category.BuildPath(parent, category.Id);

        await _audit.LogAsync(
            AuditActions.CategoryCreated,
            nameof(Category),
            category.Id.ToString(CultureInfo.InvariantCulture),
            $"Created category {category.Name}.",
            new { category.Name, category.Slug, category.ParentId, category.Depth },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(category.Id);
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            return OperationResult.Failure("That category no longer exists.");
        }

        var validation = ValidateFields(request);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var moving = category.ParentId != request.ParentId;
        Category? parent = null;

        if (moving)
        {
            var moveCheck = await ValidateMoveAsync(category, request.ParentId, cancellationToken);
            if (!moveCheck.Succeeded)
            {
                return moveCheck;
            }

            parent = await LoadParentAsync(request.ParentId, cancellationToken);
        }

        var slug = await ResolveSlugAsync(request, request.ParentId, id, cancellationToken);
        if (!slug.Succeeded)
        {
            return OperationResult.Failure(slug.Error!, slug.Field);
        }

        // A category with products under it must stay somewhere shoppers can
        // reach. Deactivating one that still holds products is allowed but
        // deliberately loud in the audit trail rather than silent.
        var before = new { category.Name, category.Slug, category.ParentId, category.IsActive };

        category.Name = request.Name.Trim();
        category.Slug = slug.Value!;
        category.Description = Trim(request.Description);
        category.ImagePath = Trim(request.ImagePath);
        category.ShowInMenu = request.ShowInMenu;
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;

        if (moving)
        {
            await RewriteSubtreeAsync(category, parent, cancellationToken);

            await _audit.LogAsync(
                AuditActions.CategoryMoved,
                nameof(Category),
                category.Id.ToString(CultureInfo.InvariantCulture),
                $"Moved category {category.Name}.",
                new { From = before.ParentId, To = request.ParentId, category.Path },
                cancellationToken: cancellationToken);
        }

        await _audit.LogAsync(
            AuditActions.CategoryUpdated,
            nameof(Category),
            category.Id.ToString(CultureInfo.InvariantCulture),
            $"Updated category {category.Name}.",
            new { Before = before, After = new { category.Name, category.Slug, category.ParentId, category.IsActive } },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Soft delete, refused while the node still has children or products.
    ///
    /// Cascading would take a whole branch of the merchandising tree with one
    /// click and orphan every product filed under it - including their primary
    /// category, which is not nullable.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (category is null)
        {
            return OperationResult.Failure("That category no longer exists.");
        }

        var childCount = await _db.Categories.CountAsync(c => c.ParentId == id, cancellationToken);

        if (childCount > 0)
        {
            return OperationResult.Failure(
                $"{category.Name} still has {childCount} sub-categor{(childCount == 1 ? "y" : "ies")}. "
                + "Move or remove them first.");
        }

        var productCount = await _db.ProductCategories.CountAsync(pc => pc.CategoryId == id, cancellationToken);

        if (productCount > 0)
        {
            return OperationResult.Failure(
                $"{productCount} product(s) are filed under {category.Name}. "
                + "Re-file them first, or deactivate this category instead.");
        }

        category.IsDeleted = true;
        category.IsActive = false;

        await _audit.LogAsync(
            AuditActions.CategoryUpdated,
            nameof(Category),
            category.Id.ToString(CultureInfo.InvariantCulture),
            $"Deleted category {category.Name}.",
            new { category.Name, Deleted = true },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Re-points a node and rewrites the stored path and depth of everything
    /// beneath it. The subtree is found by prefix on the OLD path, which is why
    /// it is captured before the node is changed.
    /// </summary>
    private async Task RewriteSubtreeAsync(
        Category category,
        Category? newParent,
        CancellationToken cancellationToken)
    {
        var oldPath = category.Path;
        var oldDepth = category.Depth;

        category.ParentId = newParent?.Id;
        category.Depth = newParent is null ? 0 : newParent.Depth + 1;
        category.Path = Category.BuildPath(newParent, category.Id);

        var descendants = await _db.Categories
            .Where(c => c.Id != category.Id && c.Path.StartsWith(oldPath))
            .ToListAsync(cancellationToken);

        var depthShift = category.Depth - oldDepth;

        foreach (var descendant in descendants)
        {
            descendant.Path = string.Concat(category.Path, descendant.Path[oldPath.Length..]);
            descendant.Depth += depthShift;
        }
    }

    private async Task<OperationResult> ValidateMoveAsync(
        Category category,
        long? newParentId,
        CancellationToken cancellationToken)
    {
        if (newParentId is null)
        {
            return await DepthWouldFit(category, 0, cancellationToken);
        }

        if (newParentId == category.Id)
        {
            return OperationResult.Failure(
                "A category cannot be its own parent.", nameof(SaveCategoryRequest.ParentId));
        }

        var parent = await LoadParentAsync(newParentId, cancellationToken);

        if (parent is null)
        {
            return OperationResult.Failure(
                "That parent category no longer exists.", nameof(SaveCategoryRequest.ParentId));
        }

        // Moving a node beneath its own descendant would detach the whole
        // subtree from the root and leave a cycle no query could terminate on.
        if (parent.Path.StartsWith(category.Path, StringComparison.Ordinal))
        {
            return OperationResult.Failure(
                $"{parent.Name} sits underneath {category.Name}, so it cannot also be its parent.",
                nameof(SaveCategoryRequest.ParentId));
        }

        return await DepthWouldFit(category, parent.Depth + 1, cancellationToken);
    }

    /// <summary>
    /// A move is refused when it would push the deepest descendant past the
    /// limit - the node itself fitting is not enough.
    /// </summary>
    private async Task<OperationResult> DepthWouldFit(
        Category category,
        int newDepth,
        CancellationToken cancellationToken)
    {
        var deepest = await _db.Categories
            .Where(c => c.Path.StartsWith(category.Path))
            .MaxAsync(c => (int?)c.Depth, cancellationToken) ?? category.Depth;

        var resultingDeepest = deepest + (newDepth - category.Depth);

        if (resultingDeepest > Category.MaxDepth)
        {
            return OperationResult.Failure(
                $"Moving {category.Name} there would push its sub-categories past the "
                + $"{Category.MaxDepth + 1}-level limit.",
                nameof(SaveCategoryRequest.ParentId));
        }

        return OperationResult.Success();
    }

    private Task<Category?> LoadParentAsync(long? parentId, CancellationToken cancellationToken) =>
        parentId is null
            ? Task.FromResult<Category?>(null)
            : _db.Categories.FirstOrDefaultAsync(c => c.Id == parentId, cancellationToken);

    private async Task<string> BuildBreadcrumbAsync(long id, CancellationToken cancellationToken)
    {
        var path = await _db.Categories
            .Where(c => c.Id == id)
            .Select(c => c.Path)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var ids = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => long.Parse(s, CultureInfo.InvariantCulture))
            .ToList();

        var names = await _db.Categories
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var byId = names.ToDictionary(n => n.Id, n => n.Name);

        return string.Join(" > ", ids.Where(byId.ContainsKey).Select(i => byId[i]));
    }

    private static OperationResult ValidateFields(SaveCategoryRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Failure("Enter a category name.", nameof(SaveCategoryRequest.Name));
        }

        if (name.Length > 150)
        {
            return OperationResult.Failure("Category name is too long.", nameof(SaveCategoryRequest.Name));
        }

        if (request.DisplayOrder is < 0 or > 9999)
        {
            return OperationResult.Failure(
                "Display order must be between 0 and 9999.", nameof(SaveCategoryRequest.DisplayOrder));
        }

        return OperationResult.Success();
    }

    private async Task<OperationResult<string>> ResolveSlugAsync(
        SaveCategoryRequest request,
        long? parentId,
        long? existingId,
        CancellationToken cancellationToken)
    {
        // Siblings only: "cleansers" is allowed under both Skincare and
        // Haircare, and forcing global uniqueness would produce
        // "cleansers-2" for no reason a shopper could see.
        var siblingSlugs = await _db.Categories
            .Where(c => c.ParentId == parentId && (existingId == null || c.Id != existingId))
            .Select(c => c.Slug)
            .ToListAsync(cancellationToken);

        var taken = siblingSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var typed = request.Slug.Trim().ToLowerInvariant();

            if (!Slug.IsValid(typed))
            {
                return OperationResult<string>.Failure(
                    "Use lowercase letters, numbers and single hyphens, for example 'night-creams'.",
                    nameof(SaveCategoryRequest.Slug));
            }

            if (taken.Contains(typed))
            {
                return OperationResult<string>.Failure(
                    $"'{typed}' is already used by another category in the same place.",
                    nameof(SaveCategoryRequest.Slug));
            }

            return OperationResult<string>.Success(typed);
        }

        var generated = Slug.From(request.Name);

        if (string.IsNullOrEmpty(generated))
        {
            return OperationResult<string>.Failure(
                "Enter a web address for this category - one could not be made from the name.",
                nameof(SaveCategoryRequest.Slug));
        }

        return OperationResult<string>.Success(Slug.MakeUnique(generated, taken.Contains));
    }

    /// <summary>
    /// Roots first, then each node's children immediately after it, each level
    /// ordered by DisplayOrder then Name. This is the order the admin table and
    /// every parent dropdown are drawn in.
    /// </summary>
    private static List<CategoryNode> OrderDepthFirst(IReadOnlyCollection<CategoryNode> all)
    {
        var byParent = all
            .GroupBy(n => n.ParentId)
            .ToDictionary(
                g => g.Key ?? 0L,
                g => g.OrderBy(n => n.DisplayOrder).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var ordered = new List<CategoryNode>(all.Count);

        void Walk(long parentKey)
        {
            if (!byParent.TryGetValue(parentKey, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                ordered.Add(child);
                Walk(child.Id);
            }
        }

        Walk(0L);

        // Anything whose parent was filtered out of the source set would be
        // unreachable by the walk. Appending it keeps the list complete rather
        // than silently losing rows.
        if (ordered.Count != all.Count)
        {
            var seen = ordered.Select(n => n.Id).ToHashSet();
            ordered.AddRange(all.Where(n => !seen.Contains(n.Id)).OrderBy(n => n.Path, StringComparer.Ordinal));
        }

        return ordered;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
