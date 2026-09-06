namespace EchoLifestyle.Web.Areas.BackOffice.Navigation;

/// <summary>
/// One entry in the back-office sidebar.
///
/// The menu is built in code rather than written into the layout, because it
/// will grow to eighteen modules. A data-driven menu can be filtered by
/// permission in one place instead of scattering @if blocks through markup.
/// </summary>
public class NavItem
{
    public required string Text { get; init; }

    /// <summary>Name of an icon defined in _Icon.cshtml.</summary>
    public string Icon { get; init; } = "dot";

    public string? Controller { get; init; }

    public string? Action { get; init; }

    /// <summary>Permission required to see this item. Null means always visible to staff.</summary>
    public string? Permission { get; init; }

    /// <summary>
    /// Modules not built yet are shown greyed out with the phase they arrive in.
    /// Being honest about what does not exist yet beats a menu of dead links.
    /// </summary>
    public string? ComingInPhase { get; init; }

    public IReadOnlyList<NavItem> Children { get; init; } = [];

    public bool IsGroup => Children.Count > 0;

    public bool IsEnabled => ComingInPhase is null;
}
