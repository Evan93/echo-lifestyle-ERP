namespace EchoLifestyle.Application.Inventory;

/// <summary>
/// Deciding which batches a quantity comes out of.
///
/// First expired, first out. Not a preference - for cosmetics and skincare it is
/// the difference between selling stock and writing it off, because the oldest
/// jar on the shelf is the one with the least time left to sell it in. Batches
/// with no expiry date fall in behind the dated ones, oldest received first.
///
/// Pure on purpose: no database, no clock, no service. Allocation is the part
/// most worth being certain about, and certainty is cheap when the function
/// takes numbers and returns numbers.
/// </summary>
public static class FefoAllocation
{
    /// <summary>One batch's availability, as the allocator sees it.</summary>
    public sealed class Candidate
    {
        public required long StockBatchId { get; init; }

        /// <summary>On hand minus what is already promised to somebody else.</summary>
        public required decimal Available { get; init; }

        /// <summary>Null sorts last: a batch with no date is not urgent.</summary>
        public DateOnly? ExpiryDate { get; init; }

        public DateOnly ReceivedDate { get; init; }

        public decimal LandedUnitCost { get; init; }
    }

    public sealed class Take
    {
        public required long StockBatchId { get; init; }

        public required decimal Quantity { get; init; }

        public decimal LandedUnitCost { get; init; }

        public decimal Value => decimal.Round(Quantity * LandedUnitCost, 4, MidpointRounding.AwayFromZero);
    }

    public sealed class Result
    {
        public List<Take> Takes { get; } = [];

        /// <summary>
        /// How much could not be found. Non-zero means the caller must refuse -
        /// there is no honest way to ship what is not there.
        /// </summary>
        public decimal Shortfall { get; init; }

        public bool IsComplete => Shortfall == 0m;

        public decimal TotalCost => Takes.Sum(t => t.Value);
    }

    /// <summary>
    /// Takes <paramref name="quantity"/> from the candidates, oldest expiry
    /// first, splitting across batches when no single one holds enough.
    /// </summary>
    public static Result Allocate(IEnumerable<Candidate> candidates, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), "Allocating zero or less is not a question worth asking.");
        }

        var ordered = candidates
            .Where(c => c.Available > 0m)

            // MaxValue rather than a null-first sort: an undated batch should be
            // used only once the dated ones are gone, because those are the ones
            // with a deadline.
            .OrderBy(c => c.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(c => c.ReceivedDate)
            .ThenBy(c => c.StockBatchId)
            .ToList();

        var remaining = quantity;
        var result = new List<Take>();

        foreach (var candidate in ordered)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var take = Math.Min(candidate.Available, remaining);

            result.Add(new Take
            {
                StockBatchId = candidate.StockBatchId,
                Quantity = take,
                LandedUnitCost = candidate.LandedUnitCost,
            });

            remaining -= take;
        }

        var allocation = new Result { Shortfall = remaining > 0m ? remaining : 0m };
        allocation.Takes.AddRange(result);

        return allocation;
    }
}
