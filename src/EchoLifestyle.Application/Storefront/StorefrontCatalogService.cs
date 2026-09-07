using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Storefront;

/// <summary>
/// What the public site is allowed to see.
///
/// A separate read service rather than reuse of the back-office query services,
/// for one reason that matters more than the duplication it costs: the rules
/// about what a customer may see are different, and they should live in one
/// place where they can be read in full. Published, active, not deleted,
/// priced. A back-office method that grew an <c>isPublic</c> flag would be one
/// forgotten argument away from putting an unpublished product on the internet.
///
/// Nothing here takes a user. There is no personalised pricing on the
/// storefront - everyone sees the default retail list - so every query is the
/// same for every visitor and is safe to cache later.
/// </summary>
public class StorefrontCatalogService
{
    /// <summary>
    /// Products per page. Chosen to fill a 4-across grid six rows deep without
    /// a pager on a catalogue this size.
    /// </summary>
    public const int PageSize = 24;

    private readonly IApplicationDbContext _db;

    public StorefrontCatalogService(IApplicationDbContext db)
    {
        _db = db;
    }

    // -----------------------------------------------------------------------
    // Navigation
    // -----------------------------------------------------------------------

    /// <summary>
    /// The category tree for the header menu: top level, with their children.
    ///
    /// Depth is capped at two by <see cref="Category.MaxDepth"/>, so this is a
    /// menu, not a recursion.
    /// </summary>
    public async Task<IReadOnlyList<ShopCategory>> GetMenuAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.ShowInMenu)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new ShopCategory
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                Slug = c.Slug,
                ImagePath = c.ImagePath,
                Depth = c.Depth,
                Path = c.Path,
            })
            .ToListAsync(cancellationToken);

        var byParent = rows
            .Where(c => c.ParentId is not null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ShopCategory>)g.ToList());

        var top = rows.Where(c => c.ParentId is null).ToList();

        foreach (var category in top)
        {
            category.Children = byParent.GetValueOrDefault(category.Id, []);
        }

        return top;
    }

    public async Task<ShopHome> GetHomeAsync(CancellationToken cancellationToken = default)
    {
        var brands = await _db.Brands
            .AsNoTracking()
            .Where(b => b.IsActive && b.IsFeatured)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .Take(12)
            .Select(b => new ShopBrand
            {
                Id = b.Id,
                Name = b.Name,
                Slug = b.Slug,
                OriginCountry = b.OriginCountry,
                LogoPath = b.LogoPath,
            })
            .ToListAsync(cancellationToken);

        var categories = await GetMenuAsync(cancellationToken);

        // One page, read twice. A second query for the offers row would cost a
        // round trip to answer a question this one already contains.
        var recent = await ListAsync(
            SellableProducts(), ShopSort.Newest, page: 1, pageSize: 40, cancellationToken);

        return new ShopHome
        {
            FeaturedBrands = brands,
            Categories = categories,
            NewArrivals = recent.Products.Take(8).ToList(),

            // Only things genuinely marked down, so the row cannot advertise a
            // discount that a stale compare-at price invented.
            OnOffer = recent.Products.Where(p => p.IsDiscounted).Take(8).ToList(),
        };
    }

    // -----------------------------------------------------------------------
    // Listings
    // -----------------------------------------------------------------------

    public async Task<(ShopCategory? Category, ShopProductPage Page)> GetCategoryAsync(
        string slug,
        ShopSort sort,
        int page,
        CancellationToken cancellationToken = default)
    {
        var category = await _db.Categories
            .AsNoTracking()
            .Where(c => c.Slug == slug && c.IsActive)
            .Select(c => new ShopCategory
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                Slug = c.Slug,
                Description = c.Description,
                ImagePath = c.ImagePath,
                Depth = c.Depth,
                Path = c.Path,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (category is null)
        {
            return (null, new ShopProductPage());
        }

        category.Children = await _db.Categories
            .AsNoTracking()
            .Where(c => c.ParentId == category.Id && c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new ShopCategory
            {
                Id = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                ImagePath = c.ImagePath,
            })
            .ToListAsync(cancellationToken);

        // Everything at or under this category. The materialised path is what
        // makes that one indexed prefix match rather than a recursive walk -
        // see the Phase 2 design record.
        var prefix = category.Path;

        var query = SellableProducts().Where(p => _db.ProductCategories
            .Any(pc => pc.ProductId == p.Id
                       && (pc.CategoryId == category.Id
                           || pc.Category!.Path.StartsWith(prefix))));

        return (category, await ListAsync(query, sort, page, PageSize, cancellationToken));
    }

    public async Task<(ShopBrand? Brand, ShopProductPage Page)> GetBrandAsync(
        string slug,
        ShopSort sort,
        int page,
        CancellationToken cancellationToken = default)
    {
        var brand = await _db.Brands
            .AsNoTracking()
            .Where(b => b.Slug == slug && b.IsActive)
            .Select(b => new ShopBrand
            {
                Id = b.Id,
                Name = b.Name,
                Slug = b.Slug,
                Description = b.Description,
                OriginCountry = b.OriginCountry,
                LogoPath = b.LogoPath,
                BannerPath = b.BannerPath,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (brand is null)
        {
            return (null, new ShopProductPage());
        }

        var query = SellableProducts().Where(p => p.BrandId == brand.Id);

        return (brand, await ListAsync(query, sort, page, PageSize, cancellationToken));
    }

    /// <summary>
    /// Search over product name, brand name and SKU.
    ///
    /// A LIKE, not a search engine. Twenty-four products do not need an index
    /// server, and the interface here is narrow enough that one can be dropped
    /// in behind it when the catalogue is large enough to justify running one.
    /// </summary>
    public async Task<ShopProductPage> SearchAsync(
        string? term,
        ShopSort sort,
        int page,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return new ShopProductPage { Page = 1, PageSize = PageSize };
        }

        var text = term.Trim();

        var query = SellableProducts().Where(p =>
            EF.Functions.Like(p.Name, $"%{text}%")
            || EF.Functions.Like(p.Brand!.Name, $"%{text}%")
            || (p.ShortDescription != null && EF.Functions.Like(p.ShortDescription, $"%{text}%"))
            || p.Variants.Any(v => v.IsActive && EF.Functions.Like(v.Sku, $"%{text}%")));

        return await ListAsync(query, sort, page, PageSize, cancellationToken);
    }

    // -----------------------------------------------------------------------
    // One product
    // -----------------------------------------------------------------------

    public async Task<ShopProductDetail?> GetProductAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var product = await SellableProducts()
            .Where(p => p.Slug == slug)
            .Select(p => new ShopProductDetail
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                ShortDescription = p.ShortDescription,
                LongDescription = p.LongDescription,
                HowToUse = p.HowToUse,
                Ingredients = p.Ingredients,
                Brand = new ShopBrand
                {
                    Id = p.Brand!.Id,
                    Name = p.Brand.Name,
                    Slug = p.Brand.Slug,
                    OriginCountry = p.Brand.OriginCountry,
                    LogoPath = p.Brand.LogoPath,
                },
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        product.Images = await _db.ProductImages
            .AsNoTracking()
            .Where(i => i.ProductId == product.Id)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.DisplayOrder)
            .Select(i => new ShopImage
            {
                Id = i.Id,
                ProductVariantId = i.ProductVariantId,
                Path = i.Path,
                AltText = i.AltText,
                IsPrimary = i.IsPrimary,
            })
            .ToListAsync(cancellationToken);

        product.Options = await _db.ProductOptions
            .AsNoTracking()
            .Where(o => o.ProductId == product.Id)
            .OrderBy(o => o.DisplayOrder)
            .Select(o => new ShopOption
            {
                Id = o.Id,
                Name = o.Name,
                Values = o.Values
                    .OrderBy(v => v.DisplayOrder)
                    .Select(v => new ShopOptionValue
                    {
                        Id = v.Id,
                        Value = v.Value,
                        SwatchHex = v.SwatchHex,
                    })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var priceListId = await DefaultPriceListIdAsync(cancellationToken);

        product.Variants = await _db.ProductVariants
            .AsNoTracking()
            .Where(v => v.ProductId == product.Id && v.IsActive)
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.DisplayOrder)
            .Select(v => new ShopVariant
            {
                Id = v.Id,
                Sku = v.Sku,
                VariantName = v.VariantName,
                CompareAtPrice = v.CompareAtPrice,
                IsDefault = v.IsDefault,

                Price = _db.PriceListItems
                    .Where(i => i.ProductVariantId == v.Id
                                && i.PriceListId == priceListId
                                && i.EffectiveToUtc == null)
                    .Select(i => (decimal?)i.UnitPrice)
                    .FirstOrDefault(),

                Available = _db.StockBalances
                    .Where(b => b.ProductVariantId == v.Id)
                    .Sum(b => (decimal?)(b.QuantityOnHand - b.QuantityReserved)) ?? 0m,

                OptionValueIds = v.OptionValues
                    .Select(ov => ov.ProductOptionValueId)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        product.Breadcrumb = await BreadcrumbAsync(product.Id, cancellationToken);

        return product;
    }

    // -----------------------------------------------------------------------
    // Shared
    // -----------------------------------------------------------------------

    /// <summary>
    /// The only definition of "a customer may see this".
    ///
    /// Published is separate from active on purpose: a product can be
    /// purchasable and stockable in the back office for months before it is
    /// meant to appear here. Soft-deleted rows are already excluded by the
    /// global query filter; the rest is stated explicitly so that reading this
    /// method tells you the whole rule.
    /// </summary>
    private IQueryable<Product> SellableProducts() =>
        _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive
                        && p.IsPublished
                        && p.Brand!.IsActive
                        && p.Variants.Any(v => v.IsActive));

    private async Task<ShopProductPage> ListAsync(
        IQueryable<Product> query,
        ShopSort sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var priceListId = await DefaultPriceListIdAsync(cancellationToken);

        // Priced separately from the projection so that sorting by price sorts
        // in the database rather than within whichever page happened to load.
        var priced = query.Select(p => new
        {
            Product = p,
            Price = _db.PriceListItems
                .Where(i => i.PriceListId == priceListId
                            && i.EffectiveToUtc == null
                            && i.ProductVariant!.ProductId == p.Id
                            && i.ProductVariant.IsActive)
                .Min(i => (decimal?)i.UnitPrice),
        });

        // An unpriced product is a merchandising mistake, not a state to show:
        // a tile with no price invites a message asking for one.
        priced = priced.Where(x => x.Price != null);

        var totalCount = await priced.CountAsync(cancellationToken);

        priced = sort switch
        {
            ShopSort.PriceLowToHigh => priced.OrderBy(x => x.Price).ThenBy(x => x.Product.Name),
            ShopSort.PriceHighToLow => priced.OrderByDescending(x => x.Price)
                                             .ThenBy(x => x.Product.Name),
            ShopSort.NameAToZ => priced.OrderBy(x => x.Product.Name),

            // Newest first, and never by Id - a product republished after being
            // pulled should come back where it belongs, not to the bottom.
            _ => priced.OrderByDescending(x => x.Product.PublishedAtUtc ?? x.Product.CreatedAtUtc)
                       .ThenByDescending(x => x.Product.Id),
        };

        if (page < 1)
        {
            page = 1;
        }

        var rows = await priced
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ShopProductCard
            {
                ProductId = x.Product.Id,
                Name = x.Product.Name,
                Slug = x.Product.Slug,
                BrandName = x.Product.Brand!.Name,
                BrandSlug = x.Product.Brand.Slug,
                PublishedAtUtc = x.Product.PublishedAtUtc,
                Price = x.Price,

                VariantCount = x.Product.Variants.Count(v => v.IsActive),

                // The highest "was" figure across the sellable variants. Shown
                // only when it beats the price, so a stale compare-at cannot
                // invent a discount that is not there.
                CompareAtPrice = x.Product.Variants
                    .Where(v => v.IsActive)
                    .Max(v => v.CompareAtPrice),

                PriceVaries = _db.PriceListItems
                    .Where(i => i.PriceListId == priceListId
                                && i.EffectiveToUtc == null
                                && i.ProductVariant!.ProductId == x.Product.Id
                                && i.ProductVariant.IsActive)
                    .Max(i => (decimal?)i.UnitPrice) != x.Price,

                Available = _db.StockBalances
                    .Where(b => b.ProductVariant!.ProductId == x.Product.Id
                                && b.ProductVariant.IsActive)
                    .Sum(b => (decimal?)(b.QuantityOnHand - b.QuantityReserved)) ?? 0m,

                ImagePath = x.Product.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.Path)
                    .FirstOrDefault(),

                ImageAlt = x.Product.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.DisplayOrder)
                    .Select(i => i.AltText)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new ShopProductPage
        {
            Products = rows,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>The product's primary category and its ancestors, outermost first.</summary>
    private async Task<IReadOnlyList<ShopCategory>> BreadcrumbAsync(
        long productId,
        CancellationToken cancellationToken)
    {
        var primary = await _db.ProductCategories
            .AsNoTracking()
            .Where(pc => pc.ProductId == productId && pc.IsPrimary)
            .Select(pc => pc.Category!)
            .Select(c => new { c.Id, c.Path })
            .FirstOrDefaultAsync(cancellationToken);

        if (primary is null)
        {
            return [];
        }

        // "/1/7/22/" - the ancestors are already in the path, so the trail is a
        // single query rather than one per level.
        var ids = primary.Path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => long.TryParse(segment, out var id) ? id : 0L)
            .Where(id => id != 0L)
            .ToList();

        if (ids.Count == 0)
        {
            ids.Add(primary.Id);
        }

        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.IsActive)
            .Select(c => new ShopCategory
            {
                Id = c.Id,
                ParentId = c.ParentId,
                Name = c.Name,
                Slug = c.Slug,
                Depth = c.Depth,
                Path = c.Path,
            })
            .ToListAsync(cancellationToken);

        return categories.OrderBy(c => c.Depth).ToList();
    }

    private async Task<long> DefaultPriceListIdAsync(CancellationToken cancellationToken) =>
        await _db.PriceLists
            .AsNoTracking()
            .Where(p => p.IsDefault && p.IsActive)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
