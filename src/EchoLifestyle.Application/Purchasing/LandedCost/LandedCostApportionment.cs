using EchoLifestyle.Domain.Purchasing;

namespace EchoLifestyle.Application.Purchasing.LandedCost;

/// <summary>
/// Spreads a shipment's charges across its lines.
///
/// Deliberately pure: no database, no entities, no clock. Landed cost decides
/// what every future margin figure is measured against, so it is worth being
/// able to test the arithmetic exhaustively in milliseconds.
/// </summary>
public static class LandedCostApportionment
{
    /// <summary>
    /// One line's contribution to the split, and where its share goes back.
    /// </summary>
    public sealed class Line
    {
        public required decimal Quantity { get; init; }

        /// <summary>Line value in BDT, after discount.</summary>
        public required decimal ValueBase { get; init; }

        /// <summary>Per-unit weight, when the variant has one recorded.</summary>
        public decimal? UnitWeightGrams { get; init; }

        /// <summary>Filled in by <see cref="Apportion"/>.</summary>
        public decimal ApportionedCharge { get; set; }
    }

    public sealed class Charge
    {
        public required decimal Amount { get; init; }

        public required ChargeApportionMethod Method { get; init; }
    }

    /// <summary>
    /// Adds each charge to the lines and returns the total apportioned.
    ///
    /// The returned total always equals the sum of the charges exactly, and the
    /// sum of the lines' shares always equals that total exactly. Rounding a
    /// three-way split of 1,000 gives 333.3333 three times and loses a hundredth
    /// of a taka; that residual is put on the largest line rather than
    /// disappearing, because a receipt whose parts do not add up to its total is
    /// a receipt nobody can reconcile.
    /// </summary>
    public static decimal Apportion(IReadOnlyList<Line> lines, IReadOnlyList<Charge> charges)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(charges);

        foreach (var line in lines)
        {
            line.ApportionedCharge = 0m;
        }

        if (lines.Count == 0)
        {
            return 0m;
        }

        var total = 0m;

        foreach (var charge in charges)
        {
            if (charge.Amount == 0m)
            {
                continue;
            }

            ApportionOne(lines, charge);
            total += charge.Amount;
        }

        return total;
    }

    private static void ApportionOne(IReadOnlyList<Line> lines, Charge charge)
    {
        var weights = Weigh(lines, charge.Method);
        var weightTotal = weights.Sum();

        // Every fallback has already been tried by this point, so an equal
        // split is the only thing left that is not simply wrong.
        if (weightTotal == 0m)
        {
            weights = [.. Enumerable.Repeat(1m, lines.Count)];
            weightTotal = lines.Count;
        }

        var allocated = 0m;
        var largest = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var share = decimal.Round(charge.Amount * weights[i] / weightTotal, 4, MidpointRounding.AwayFromZero);

            lines[i].ApportionedCharge += share;
            allocated += share;

            if (weights[i] > weights[largest])
            {
                largest = i;
            }
        }

        // Whatever rounding lost or gained lands on the line best able to
        // absorb it without visibly distorting its unit cost.
        lines[largest].ApportionedCharge += charge.Amount - allocated;
    }

    private static decimal[] Weigh(IReadOnlyList<Line> lines, ChargeApportionMethod method)
    {
        switch (method)
        {
            case ChargeApportionMethod.ByQuantity:
                return [.. lines.Select(l => l.Quantity)];

            case ChargeApportionMethod.ByWeight:
                var weights = lines.Select(l => l.Quantity * (l.UnitWeightGrams ?? 0m)).ToArray();

                // Weighing works only if somebody recorded weights. Falling back
                // to quantity beats splitting a freight bill equally across a
                // pallet and a lipstick.
                return weights.Sum() == 0m ? [.. lines.Select(l => l.Quantity)] : weights;

            default:
                var values = lines.Select(l => l.ValueBase).ToArray();

                // A wholly free shipment - samples, replacements - has no value
                // to split by.
                return values.Sum() == 0m ? [.. lines.Select(l => l.Quantity)] : values;
        }
    }

    /// <summary>
    /// True landed cost per unit: what the goods cost plus their share of
    /// getting them here.
    /// </summary>
    public static decimal UnitCost(decimal valueBase, decimal apportionedCharge, decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity), quantity, "A receipt line must have a positive quantity.");
        }

        return decimal.Round((valueBase + apportionedCharge) / quantity, 4, MidpointRounding.AwayFromZero);
    }
}
