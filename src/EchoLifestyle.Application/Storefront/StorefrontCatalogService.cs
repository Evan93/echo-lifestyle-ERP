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
    /// How many brands a single menu panel will list. Past this it stops being
    /// a shortcut and becomes a second catalogue to read; the category page's
    /// own sidebar has the complete list.
    /// </summary>
    private const int MenuBrandLimit = 8;

    /// <summary>
    /// The category tree for the header menu: top level, with their children
    /// and the brands stocked beneath them.
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

        var brands = await MenuBrandsAsync(cancellationToken);

        foreach (var category in top)
        {
            category.Children = byParent.GetValueOrDefault(category.Id, []);
            category.Brands = brands.GetValueOrDefault(category.Id, []);
        }

        return top;
    }

    /// <summary>
    /// Which brands sit under each top-level category, and how many products of
    /// theirs a shopper would find there.
    ///
    /// One query for the whole menu rather than one per category. Every row a
    /// product sits in already carries its category's materialised path, and
    /// the first segment of that path is the top-level ancestor - so the
    /// grouping is arithmetic on strings already fetched, not a recursive walk
    /// nor a query per heading.
    ///
    /// The grouping itself is done in memory on purpose. It is over the
    /// category rows of sellable products only, which for a catalogue of this
    /// size is a few hundred rows at most, and doing it here keeps the whole
    /// rule readable in one place. If the catalogue ever grows past that, this
    /// is the method to push down into SQL - the shape of what it returns need
    /// not change.
    /// </summary>
    private async Task<Dictionary<long, IReadOnlyList<BrandFacet>>> MenuBrandsAsync(
        CancellationToken cancellationToken)
    {
        var sellable = SellableProducts();

        var rows = await _db.ProductCategories
            .AsNoTracking()
            .Where(pc => pc.Category!.IsActive && sellable.Any(p => p.Id == pc.ProductId))
            .Select(pc => new
            {
                pc.ProductId,
                pc.Category!.Path,
                pc.Product!.BrandId,
                BrandName = pc.Product.Brand!.Name,
                BrandSlug = pc.Product.Brand.Slug,
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<long, IReadOnlyList<BrandFacet>>();

        foreach (var group in rows.GroupBy(r => RootCategoryId(r.Path)))
        {
            if (group.Key is not { } rootId)
            {
                continue;
            }

            result[rootId] = group
                .GroupBy(r => new { r.BrandId, r.BrandName, r.BrandSlug })
                .Select(g => new BrandFacet
                {
                    Id = g.Key.BrandId,
                    Name = g.Key.BrandName,
                    Slug = g.Key.BrandSlug,

                    // Distinct, because a product filed under both a parent and
                    // its child appears twice in the rows above and is still
                    // one product on the page the shopper lands on.
                    Count = g.Select(x => x.ProductId).Distinct().Count(),
                })
                .OrderByDescending(b => b.Count)
                .ThenBy(b => b.Name)
                .Take(MenuBrandLimit)
                .ToList();
        }

        return result;
    }

    /// <summary>
    /// The top-level ancestor out of a materialised path such as "/1/7/22/".
    /// Null when the path is malformed, which is a row to skip rather than a
    /// menu to fail on.
    /// </summary>
    private static long? RootCategoryId(string path)
    {
        var first = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return long.TryParse(first, out var id) && id > 0 ? id : null;
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

    public async Task<(ShopCategory? Category, ShopProductPage Page, ShopFacets Facets)>
        GetCategoryAsync(
            string slug,
            ShopSort sort,
            int page,
            ShopFilters? filters = null,
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
            return (null, new ShopProductPage(), new ShopFacets());
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

        var facets = await FacetsAsync(query, cancellationToken);
        var products = await ListAsync(query, sort, page, PageSize, cancellationToken, filters);

        return (category, products, facets);
    }

    public async Task<(ShopBrand? Brand, ShopProductPage Page, ShopFacets Facets)> GetBrandAsync(
        string slug,
        ShopSort sort,
        int page,
        ShopFilters? filters = null,
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
            return (null, new ShopProductPage(), new ShopFacets());
        }

        var query = SellableProducts().Where(p => p.BrandId == brand.Id);

        var facets = await FacetsAsync(query, cancellationToken);
        var products = await ListAsync(query, sort, page, PageSize, cancellationToken, filters);

        return (brand, products, facets);
    }

    /// <summary>
    /// Search over product name, brand name and SKU.
    ///
    /// A LIKE, not a search engine. Twenty-four products do not need an index
    /// server, and the interface here is narrow enough that one can be dropped
    /// in behind it when the catalogue is large enough to justify running one.
    /// </summary>
    public async Task<(ShopProductPage Page, ShopFacets Facets)> SearchAsync(
        string? term,
        ShopSort sort,
        int page,
        ShopFilters? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return (new ShopProductPage { Page = 1, PageSize = PageSize }, new ShopFacets());
        }

        var text = term.Trim();

        var query = SellableProducts().Where(p =>
            EF.Functions.Like(p.Name, $"%{text}%")
            || EF.Functions.Like(p.Brand!.Name, $"%{text}%")
            || (p.ShortDescription != null && EF.Functions.Like(p.ShortDescription, $"%{text}%"))
            || p.Variants.Any(v => v.IsActive && EF.Functions.Like(v.Sku, $"%{text}%")));

        var facets = await FacetsAsync(query, cancellationToken);

        return (await ListAsync(query, sort, page, PageSize, cancellationToken, filters), facets);
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

    /// <summary>
    /// What is worth offering to filter by, over the products in scope.
    ///
    /// Deliberately computed before any filter is applied. Recomputing the
    /// brand list from the already-filtered set would make every brand but the
    /// chosen one disappear, and leave somebody unable to widen their own
    /// search without pressing back.
    /// </summary>
    private async Task<ShopFacets> FacetsAsync(
        IQueryable<Product> query,
        CancellationToken cancellationToken)
    {
        var priceListId = await DefaultPriceListIdAsync(cancellationToken);

        var brands = await query
            .GroupBy(p => new { p.BrandId, p.Brand!.Name, p.Brand.Slug })
            .Select(g => new BrandFacet
            {
                Id = g.Key.BrandId,
                Name = g.Key.Name,
                Slug = g.Key.Slug,
                Count = g.Count(),
            })
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);

        var prices = await query
            .Select(p => _db.PriceListItems
                .Where(i => i.PriceListId == priceListId
                            && i.EffectiveToUtc == null
                            && i.ProductVariant!.ProductId == p.Id
                            && i.ProductVariant.IsActive)
                .Min(i => (decimal?)i.UnitPrice))
            .Where(price => price != null)
            .ToListAsync(cancellationToken);

        return new ShopFacets
        {
            Brands = brands,
            LowestPrice = prices.Count == 0 ? null : prices.Min(),
            HighestPrice = prices.Count == 0 ? null : prices.Max(),
        };
    }

    private async Task<ShopProductPage> ListAsync(
        IQueryable<Product> query,
        ShopSort sort,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        ShopFilters? filters = null)
    {
        var priceListId = await DefaultPriceListIdAsync(cancellationToken);

        if (filters?.BrandIds.Count > 0)
        {
            var brandIds = filters.BrandIds;
            query = query.Where(p => brandIds.Contains(p.BrandId));
        }

        if (filters?.InStockOnly == true)
        {
            // Availability, not on-hand (rule 18). Stock already promised to
            // somebody else is not what this shopper can buy.
            query = query.Where(p => _db.StockBalances
                .Where(b => b.ProductVariant!.ProductId == p.Id && b.ProductVariant.IsActive)
                .Sum(b => (decimal?)(b.QuantityOnHand - b.QuantityReserved)) > 0m);
        }

        if (filters?.OnOfferOnly == true)
        {
            // A compare-at price only counts as an offer while it is genuinely
            // above what the thing costs today. A stale one is not a discount.
            query = query.Where(p => p.Variants
                .Where(v => v.IsActive)
                .Max(v => v.CompareAtPrice) > _db.PriceListItems
                    .Where(i => i.PriceListId == priceListId
                                && i.EffectiveToUtc == null
                                && i.ProductVariant!.ProductId == p.Id
                                && i.ProductVariant.IsActive)
                    .Min(i => (decimal?)i.UnitPrice));
        }

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

        // Applied here rather than above, because "the price" of a product with
        // several variants is the cheapest of them - which only exists once the
        // projection has worked it out.
        if (filters?.MinPrice is { } min)
        {
            priced = priced.Where(x => x.Price >= min);
        }

        if (filters?.MaxPrice is { } max)
        {
            priced = priced.Where(x => x.Price <= max);
        }

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
