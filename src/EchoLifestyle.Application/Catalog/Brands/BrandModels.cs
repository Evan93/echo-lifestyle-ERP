namespace EchoLifestyle.Application.Catalog.Brands;

public class BrandListItem
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? OriginCountry { get; set; }

    public string? LogoPath { get; set; }

    public bool IsFeatured { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    /// <summary>
    /// Shown in the grid so it is obvious which brands can safely be
    /// deactivated and which have a catalogue behind them.
    /// </summary>
    public int ProductCount { get; set; }
}

public class BrandDetail
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? OriginCountry { get; set; }

    public string? LogoPath { get; set; }

    public string? BannerPath { get; set; }

    public bool IsFeatured { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public int ProductCount { get; set; }
}

public class SaveBrandRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Left blank on create, the service generates one from the name. Once set
    /// it is the caller's to change deliberately - it is a public URL.
    /// </summary>
    public string? Slug { get; set; }

    public string? Description { get; set; }

    public string? OriginCountry { get; set; }

    public string? LogoPath { get; set; }

    public string? BannerPath { get; set; }

    public bool IsFeatured { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Brand id and name, for dropdowns.</summary>
public class BrandOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
