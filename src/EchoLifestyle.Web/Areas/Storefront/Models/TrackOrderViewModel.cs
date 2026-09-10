using EchoLifestyle.Application.Storefront;

namespace EchoLifestyle.Web.Areas.Storefront.Models;

public class TrackOrderViewModel
{
    public string? OrderNumber { get; set; }

    public string? Phone { get; set; }

    /// <summary>
    /// Whether somebody actually asked, as opposed to landing on the page.
    /// Without this the empty form would greet every first-time visitor with
    /// "we could not find that order", which is a message about nothing.
    /// </summary>
    public bool Searched { get; set; }

    public TrackedOrder? Order { get; set; }

    public bool NotFound => Searched && Order is null;
}
