using System.Text;
using System.Xml;
using EchoLifestyle.Application.Storefront;
using Microsoft.AspNetCore.Mvc;

namespace EchoLifestyle.Web.Areas.Storefront.Controllers;

/// <summary>
/// The two files written for machines rather than people.
///
/// Both are generated rather than static, because both have to stay true as
/// the catalogue changes. A sitemap.xml checked into wwwroot is accurate for
/// exactly as long as nobody adds a product.
/// </summary>
public class SeoController : StorefrontControllerBase
{
    private readonly StorefrontCatalogService _catalog;

    public SeoController(StorefrontCatalogService catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    /// Written with <see cref="XmlWriter"/> rather than a Razor view or string
    /// concatenation. A slug containing an ampersand would silently produce a
    /// malformed document that search engines reject whole - and the failure
    /// would be invisible until somebody wondered why the site was not being
    /// indexed. The writer escapes; a template does not.
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Sitemap(CancellationToken cancellationToken)
    {
        var sitemap = await _catalog.GetSitemapAsync(cancellationToken);

        var buffer = new StringWriter();

        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,
            Encoding = new UTF8Encoding(false),
        };

        await using (var writer = XmlWriter.Create(buffer, settings))
        {
            await writer.WriteStartDocumentAsync();
            await writer.WriteStartElementAsync(
                null, "urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            // The pages that always exist. Priorities are deliberately left
            // out: search engines have ignored them for years, and a file full
            // of invented numbers is a file that has to be maintained.
            await WriteAsync(writer, Url.RouteUrl("storefront-home", null, Request.Scheme), null);
            await WriteAsync(writer, Url.RouteUrl("storefront-delivery-page", null, Request.Scheme), null);
            await WriteAsync(writer, Url.RouteUrl("storefront-returns", null, Request.Scheme), null);
            await WriteAsync(writer, Url.RouteUrl("storefront-contact", null, Request.Scheme), null);
            await WriteAsync(writer, Url.RouteUrl("storefront-privacy", null, Request.Scheme), null);

            foreach (var entry in sitemap.Categories)
            {
                await WriteAsync(
                    writer,
                    Url.RouteUrl("storefront-category", new { slug = entry.Slug }, Request.Scheme),
                    entry.LastModifiedUtc);
            }

            foreach (var entry in sitemap.Brands)
            {
                await WriteAsync(
                    writer,
                    Url.RouteUrl("storefront-brand", new { slug = entry.Slug }, Request.Scheme),
                    entry.LastModifiedUtc);
            }

            foreach (var entry in sitemap.Products)
            {
                await WriteAsync(
                    writer,
                    Url.RouteUrl("storefront-product", new { slug = entry.Slug }, Request.Scheme),
                    entry.LastModifiedUtc);
            }

            await writer.WriteEndElementAsync();
            await writer.WriteEndDocumentAsync();
        }

        return Content(buffer.ToString(), "application/xml", Encoding.UTF8);
    }

    private static async Task WriteAsync(XmlWriter writer, string? location, DateTime? lastModified)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return;
        }

        await writer.WriteStartElementAsync(null, "url", null);
        await writer.WriteElementStringAsync(null, "loc", null, location);

        if (lastModified is { } modified)
        {
            // W3C date, UTC. The value comes from the row, so a page that has
            // not changed does not claim to have.
            await writer.WriteElementStringAsync(
                null, "lastmod", null,
                DateTime.SpecifyKind(modified, DateTimeKind.Utc).ToString("yyyy-MM-dd"));
        }

        await writer.WriteEndElementAsync();
    }

    /// <summary>
    /// What crawlers should leave alone.
    ///
    /// Three kinds of thing are disallowed, for three different reasons. The
    /// back office is private. Cart, checkout and order-received are per-visitor
    /// pages that mean nothing to anybody else. Search results are an infinite
    /// space of thin pages that dilute the ones that matter - and tracking URLs
    /// carry somebody's order number in the query string.
    ///
    /// None of this is a security control. Robots.txt is a request, and a
    /// crawler that ignores it still gets whatever the server would have
    /// served - which is why the back office authorises every request on the
    /// server and the tracking page needs both halves of its credential.
    /// </summary>
    [HttpGet]
    public IActionResult Robots()
    {
        var sitemap = Url.RouteUrl("storefront-sitemap", null, Request.Scheme);

        var text = $"""
            User-agent: *
            Disallow: /BackOffice
            Disallow: /cart
            Disallow: /checkout
            Disallow: /order-received
            Disallow: /track
            Disallow: /search

            Sitemap: {sitemap}

            """;

        return Content(text, "text/plain", Encoding.UTF8);
    }
}
