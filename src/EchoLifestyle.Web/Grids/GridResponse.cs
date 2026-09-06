using EchoLifestyle.Application.Common.Results;

namespace EchoLifestyle.Web.Grids;

/// <summary>
/// The shape DataTables expects back. Property names are lower-cased by the
/// JSON serialiser's camelCase policy, which is what the client reads.
/// </summary>
public class GridResponse<T>
{
    public int Draw { get; init; }

    public int RecordsTotal { get; init; }

    public int RecordsFiltered { get; init; }

    public IReadOnlyList<T> Data { get; init; } = [];

    public static GridResponse<T> From(GridRequest request, PagedResult<T> page) => new()
    {
        Draw = request.Draw,
        RecordsTotal = page.TotalCount,
        RecordsFiltered = page.FilteredCount,
        Data = page.Rows,
    };
}
