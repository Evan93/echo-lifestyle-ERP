namespace EchoLifestyle.Web.Grids;

/// <summary>
/// What a grid asks the server for.
///
/// DataTables' own wire format sends nested arrays (columns[0][data]) that MVC
/// cannot bind without a custom binder. Since we control the client, echo-grid.js
/// flattens the request into these few fields instead - simpler to bind, simpler
/// to read in a controller, and it keeps the sortable surface explicit.
/// </summary>
public class GridRequest
{
    /// <summary>DataTables' echo counter; returned unchanged so it can discard stale responses.</summary>
    public int Draw { get; set; }

    /// <summary>Rows to skip.</summary>
    public int Start { get; set; }

    /// <summary>Page size. Clamped server-side - the client does not get to ask for everything.</summary>
    public int Length { get; set; } = 25;

    public string? Search { get; set; }

    /// <summary>
    /// Column key to sort by. Never interpolated into SQL: each grid maps the
    /// key to an expression through an explicit switch, so an unknown value
    /// falls back to the default order rather than reaching the database.
    /// </summary>
    public string? SortColumn { get; set; }

    public string? SortDirection { get; set; }

    public bool SortDescending =>
        string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Page size, clamped to something a browser can actually render.</summary>
    public int PageSize => Length switch
    {
        <= 0 => 25,
        > 200 => 200,
        _ => Length,
    };

    public int Skip => Start < 0 ? 0 : Start;

    public string? NormalisedSearch =>
        string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}
