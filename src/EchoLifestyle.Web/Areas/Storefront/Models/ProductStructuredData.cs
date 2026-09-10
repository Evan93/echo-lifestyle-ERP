using System.Text.Json;
using EchoLifestyle.Application.Storefront;

namespace EchoLifestyle.Web.Areas.Storefront.Models;

/// <summary>
/// schema.org Product, as JSON-LD.
///
/// This is what lets a search result carry a price and "in stock" underneath
/// the link rather than a bare title, which for a shop is most of the value of
/// being in the results at all.
///
/// Built here in C# and serialised, never assembled as a string in the view.
/// A product name containing a quotation mark - "Nature's" is not exotic in
/// this catalogue - would break a hand-written JSON block, and the failure is
/// silent: the page renders, the block is invalid, and the rich result quietly
/// never appears. <see cref="JsonSerializer"/> escapes correctly, and its
/// default encoder also escapes the characters that would otherwise let a
/// product name close the surrounding script tag.
/// </summary>
public static class ProductStructuredData
{
    public static string Build(
        ShopProductDetail product,
        string productUrl,
        string? imageUrl,
        string? brandUrl)
    {
        var prices = product.Variants
            .Where(v => v.Price is not null)
            .Select(v => v.Price!.Value)
            .ToList();

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["name"] = product.Name,
            ["url"] = productUrl,
            ["description"] = product.ShortDescription,
            ["brand"] = new Dictionary<string, object?>
            {
                ["@type"] = "Brand",
                ["name"] = product.Brand.Name,
                ["url"] = brandUrl,
            },
        };

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            data["image"] = imageUrl;
        }

        // A single-variant product has one SKU worth stating. On a product with
        // shades there is no one SKU, and asserting the default variant's would
        // be wrong for every other shade.
        if (product.Variants.Count == 1)
        {
            data["sku"] = product.Variants[0].Sku;
        }

        var availability = product.InStock
            ? "https://schema.org/InStock"
            : "https://schema.org/OutOfStock";

        if (prices.Count == 0)
        {
            // Nothing priced. The offers block is left out entirely rather than
            // published with a zero - a price of nought in a search result is
            // worse than no price at all.
        }
        else if (prices.Min() == prices.Max())
        {
            data["offers"] = new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["price"] = prices[0],
                ["priceCurrency"] = "BDT",
                ["availability"] = availability,
                ["url"] = productUrl,
            };
        }
        else
        {
            data["offers"] = new Dictionary<string, object?>
            {
                ["@type"] = "AggregateOffer",
                ["lowPrice"] = prices.Min(),
                ["highPrice"] = prices.Max(),
                ["offerCount"] = prices.Count,
                ["priceCurrency"] = "BDT",
                ["availability"] = availability,
                ["url"] = productUrl,
            };
        }

        return JsonSerializer.Serialize(data, Options);
    }

    /// <summary>
    /// Organization, for the home page. Gives a search engine the name, logo
    /// and social profiles to attach to the brand rather than inferring them.
    /// </summary>
    public static string BuildOrganization(
        string name,
        string siteUrl,
        string logoUrl,
        string? phone,
        IEnumerable<string> socialUrls)
    {
        var profiles = socialUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Organization",
            ["name"] = name,
            ["url"] = siteUrl,
            ["logo"] = logoUrl,
        };

        if (!string.IsNullOrWhiteSpace(phone))
        {
            data["telephone"] = phone;
        }

        if (profiles.Count > 0)
        {
            data["sameAs"] = profiles;
        }

        return JsonSerializer.Serialize(data, Options);
    }

    /// <summary>
    /// Nulls dropped rather than emitted. <c>"description": null</c> is not
    /// wrong exactly, but it is noise in a block that is read by a validator.
    /// The default HTML-safe encoder is kept deliberately - see the class note.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
