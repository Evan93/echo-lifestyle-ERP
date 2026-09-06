namespace EchoLifestyle.Application.Common.Results;

/// <summary>
/// One page of rows plus the counts a grid needs to render its pager.
///
/// Every list in this system is paged at the database. Loading a whole table
/// into memory to count or sort it works fine on twenty products and falls over
/// on twenty thousand.
/// </summary>
public class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> rows, int totalCount, int filteredCount)
    {
        Rows = rows;
        TotalCount = totalCount;
        FilteredCount = filteredCount;
    }

    public IReadOnlyList<T> Rows { get; }

    /// <summary>Rows available before any search term was applied.</summary>
    public int TotalCount { get; }

    /// <summary>Rows matching the current search.</summary>
    public int FilteredCount { get; }

    public static PagedResult<T> Empty() => new([], 0, 0);
}
