using System.Globalization;
using EchoLifestyle.Application.Common.Filters;
using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Application.Common.Text;
using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Catalog.Brands;

/// <summary>
/// Brand administration.
///
/// Brands are not branch-scoped: the catalogue is company-wide, and a brand
/// carried by one branch is the same brand everywhere. There is deliberately no
/// CanAccessBranch check here.
/// </summary>
public class BrandAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public BrandAdminService(IApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PagedResult<BrandListItem>> ListAsync(
        string? search,
        int skip,
        int take,
        string? sortColumn,
        bool sortDescending,
        StatusFilter status,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Brands.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        query = status switch
        {
            StatusFilter.Active => query.Where(b => b.IsActive),
            StatusFilter.Inactive => query.Where(b => !b.IsActive),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b =>
                EF.Functions.Like(b.Name, $"%{term}%")
                || (b.OriginCountry != null && EF.Functions.Like(b.OriginCountry, $"%{term}%")));
        }

        var filteredCount = await query.CountAsync(cancellationToken);

        query = (sortColumn, sortDescending) switch
        {
            ("name", true) => query.OrderByDescending(b => b.Name),
            ("origin", false) => query.OrderBy(b => b.OriginCountry).ThenBy(b => b.Name),
            ("origin", true) => query.OrderByDescending(b => b.OriginCountry).ThenBy(b => b.Name),
            ("featured", false) => query.OrderBy(b => b.IsFeatured).ThenBy(b => b.DisplayOrder),
            ("featured", true) => query.OrderByDescending(b => b.IsFeatured).ThenBy(b => b.DisplayOrder),
            ("order", false) => query.OrderBy(b => b.DisplayOrder).ThenBy(b => b.Name),
            ("order", true) => query.OrderByDescending(b => b.DisplayOrder).ThenBy(b => b.Name),
            ("isActive", false) => query.OrderBy(b => b.IsActive).ThenBy(b => b.Name),
            ("isActive", true) => query.OrderByDescending(b => b.IsActive).ThenBy(b => b.Name),
            _ => query.OrderBy(b => b.Name),
        };

        var rows = await query
            .Skip(skip)
            .Take(take)
            .Select(b => new BrandListItem
            {
                Id = b.Id,
                Name = b.Name,
                Slug = b.Slug,
                OriginCountry = b.OriginCountry,
                LogoPath = b.LogoPath,
                IsFeatured = b.IsFeatured,
                DisplayOrder = b.DisplayOrder,
                IsActive = b.IsActive,
                ProductCount = b.Products.Count,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<BrandListItem>(rows, totalCount, filteredCount);
    }

    public async Task<BrandDetail?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _db.Brands
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BrandDetail
            {
                Id = b.Id,
                Name = b.Name,
                Slug = b.Slug,
                Description = b.Description,
                OriginCountry = b.OriginCountry,
                LogoPath = b.LogoPath,
                BannerPath = b.BannerPath,
                IsFeatured = b.IsFeatured,
                DisplayOrder = b.DisplayOrder,
                IsActive = b.IsActive,
                ProductCount = b.Products.Count,
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Active brands for the product form's dropdown.</summary>
    public async Task<IReadOnlyList<BrandOption>> GetOptionsAsync(
        long? includeInactiveId = null,
        CancellationToken cancellationToken = default)
    {
        // An inactive brand still has to appear when editing a product that
        // already uses it - otherwise saving that product would silently move
        // it to whichever brand happened to be first in the list.
        return await _db.Brands
            .AsNoTracking()
            .Where(b => b.IsActive || b.Id == includeInactiveId)
            .OrderBy(b => b.Name)
            .Select(b => new BrandOption { Id = b.Id, Name = b.Name, IsActive = b.IsActive })
            .ToListAsync(cancellationToken);
    }

    public async Task<OperationResult<long>> CreateAsync(
        SaveBrandRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, existingId: null, cancellationToken);
        if (!validation.Succeeded)
        {
            return OperationResult<long>.Failure(validation.Error!, validation.Field);
        }

        var slug = await ResolveSlugAsync(request, existingId: null, cancellationToken);
        if (!slug.Succeeded)
        {
            return OperationResult<long>.Failure(slug.Error!, slug.Field);
        }

        var brand = new Brand
        {
            Name = request.Name.Trim(),
            Slug = slug.Value!,
            Description = Trim(request.Description),
            OriginCountry = Trim(request.OriginCountry),
            LogoPath = Trim(request.LogoPath),
            BannerPath = Trim(request.BannerPath),
            IsFeatured = request.IsFeatured,
            DisplayOrder = request.DisplayOrder,
            IsActive = request.IsActive,
        };

        _db.Brands.Add(brand);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            AuditActions.BrandCreated,
            nameof(Brand),
            brand.Id.ToString(CultureInfo.InvariantCulture),
            $"Created brand {brand.Name}.",
            new { brand.Name, brand.Slug, brand.OriginCountry },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult<long>.Success(brand.Id);
    }

    public async Task<OperationResult> UpdateAsync(
        long id,
        SaveBrandRequest request,
        CancellationToken cancellationToken = default)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (brand is null)
        {
            return OperationResult.Failure("That brand no longer exists.");
        }

        var validation = await ValidateAsync(request, id, cancellationToken);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var slug = await ResolveSlugAsync(request, id, cancellationToken);
        if (!slug.Succeeded)
        {
            return OperationResult.Failure(slug.Error!, slug.Field);
        }

        // Deactivating a brand hides it from the storefront and from the
        // product form. Products already filed under it keep working - they
        // are not orphaned - so this is a warning-free operation. Deleting is
        // the one that has to be guarded; see DeleteAsync.
        var before = new { brand.Name, brand.Slug, brand.IsActive, brand.IsFeatured };

        brand.Name = request.Name.Trim();
        brand.Slug = slug.Value!;
        brand.Description = Trim(request.Description);
        brand.OriginCountry = Trim(request.OriginCountry);
        brand.LogoPath = Trim(request.LogoPath);
        brand.BannerPath = Trim(request.BannerPath);
        brand.IsFeatured = request.IsFeatured;
        brand.DisplayOrder = request.DisplayOrder;
        brand.IsActive = request.IsActive;

        await _audit.LogAsync(
            AuditActions.BrandUpdated,
            nameof(Brand),
            brand.Id.ToString(CultureInfo.InvariantCulture),
            $"Updated brand {brand.Name}.",
            new { Before = before, After = new { brand.Name, brand.Slug, brand.IsActive, brand.IsFeatured } },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    /// <summary>
    /// Soft delete. Refused while any product still references the brand,
    /// because the product form would then show a blank brand and the
    /// storefront would render a dead link.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        if (brand is null)
        {
            return OperationResult.Failure("That brand no longer exists.");
        }

        var productCount = await _db.Products.CountAsync(p => p.BrandId == id, cancellationToken);

        if (productCount > 0)
        {
            return OperationResult.Failure(
                $"{productCount} product(s) are filed under {brand.Name}. Move them to another brand first, "
                + "or deactivate this brand instead - deactivating hides it without breaking those products.");
        }

        brand.IsDeleted = true;
        brand.IsActive = false;

        await _audit.LogAsync(
            AuditActions.BrandUpdated,
            nameof(Brand),
            brand.Id.ToString(CultureInfo.InvariantCulture),
            $"Deleted brand {brand.Name}.",
            new { brand.Name, Deleted = true },
            cancellationToken: cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return OperationResult.Success();
    }

    private async Task<OperationResult> ValidateAsync(
        SaveBrandRequest request,
        long? existingId,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Failure("Enter a brand name.", nameof(SaveBrandRequest.Name));
        }

        if (name.Length > 150)
        {
            return OperationResult.Failure("Brand name is too long.", nameof(SaveBrandRequest.Name));
        }

        var nameTaken = await _db.Brands.AnyAsync(
            b => b.Name == name && (existingId == null || b.Id != existingId),
            cancellationToken);

        if (nameTaken)
        {
            return OperationResult.Failure(
                $"'{name}' is already a brand.", nameof(SaveBrandRequest.Name));
        }

        if (request.DisplayOrder is < 0 or > 9999)
        {
            return OperationResult.Failure(
                "Display order must be between 0 and 9999.", nameof(SaveBrandRequest.DisplayOrder));
        }

        return OperationResult.Success();
    }

    /// <summary>
    /// Uses the slug the user typed, or derives one from the name and makes it
    /// unique. A typed slug is never silently altered - if it collides the save
    /// is refused, because quietly turning "cetaphil" into "cetaphil-2" would
    /// publish an address nobody chose.
    /// </summary>
    private async Task<OperationResult<string>> ResolveSlugAsync(
        SaveBrandRequest request,
        long? existingId,
        CancellationToken cancellationToken)
    {
        var taken = await _db.Brands
            .Where(b => existingId == null || b.Id != existingId)
            .Select(b => b.Slug)
            .ToListAsync(cancellationToken);

        var takenSet = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var typed = request.Slug.Trim().ToLowerInvariant();

            if (!Slug.IsValid(typed))
            {
                return OperationResult<string>.Failure(
                    "Use lowercase letters, numbers and single hyphens, for example 'the-ordinary'.",
                    nameof(SaveBrandRequest.Slug));
            }

            if (takenSet.Contains(typed))
            {
                return OperationResult<string>.Failure(
                    $"The address '{typed}' is already used by another brand.",
                    nameof(SaveBrandRequest.Slug));
            }

            return OperationResult<string>.Success(typed);
        }

        var generated = Slug.From(request.Name);

        if (string.IsNullOrEmpty(generated))
        {
            // Happens when the name is entirely non-Latin - Bangla, for
            // instance. The user has to supply an address themselves.
            return OperationResult<string>.Failure(
                "Enter a web address for this brand - one could not be made from the name.",
                nameof(SaveBrandRequest.Slug));
        }

        return OperationResult<string>.Success(Slug.MakeUnique(generated, takenSet.Contains));
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
