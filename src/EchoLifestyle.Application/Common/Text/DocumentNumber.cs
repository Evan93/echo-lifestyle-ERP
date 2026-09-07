using System.Globalization;

namespace EchoLifestyle.Application.Common.Text;

/// <summary>
/// Document numbers: GRN-2609-0001, ADJ-2609-0004, CNT-2609-0002.
///
/// A month prefix with a sequence that restarts each month. The alternative -
/// one number that only ever goes up - is tidier in the database and useless on
/// a shelf, where somebody is trying to find the delivery from last September.
///
/// The sequence is read inside the posting transaction, so two people numbering
/// at once cannot take the same value: the second waits, reads the first, and
/// takes the next. A unique index on the number column is the backstop, because
/// this is a read-then-write and no amount of care in application code makes
/// that atomic on its own.
/// </summary>
public static class DocumentNumber
{
    public const string GoodsReceipt = "GRN";
    public const string StockAdjustment = "ADJ";
    public const string StockCount = "CNT";

    /// <summary>"ADJ-2609-" - what every number for that code and month starts with.</summary>
    public static string Prefix(string code, DateOnly date) =>
        $"{code}-{date:yyMM}-";

    /// <summary>
    /// The next number after those already used.
    /// </summary>
    /// <param name="prefix">From <see cref="Prefix"/>.</param>
    /// <param name="used">
    /// Every existing number sharing that prefix. Anything not ending in a plain
    /// integer counts as zero rather than throwing - a hand-edited row should
    /// not stop the business receiving stock.
    /// </param>
    public static string Next(string prefix, IEnumerable<string> used)
    {
        ArgumentNullException.ThrowIfNull(used);

        var highest = used
            .Select(number =>
            {
                if (number.Length <= prefix.Length)
                {
                    return 0;
                }

                return int.TryParse(
                    number[prefix.Length..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value)
                    ? value
                    : 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highest + 1:D4}";
    }
}
