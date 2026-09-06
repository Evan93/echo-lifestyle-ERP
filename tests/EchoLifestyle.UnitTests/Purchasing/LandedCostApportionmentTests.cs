using EchoLifestyle.Application.Purchasing.LandedCost;
using EchoLifestyle.Domain.Purchasing;

namespace EchoLifestyle.UnitTests.Purchasing;

public class LandedCostApportionmentTests
{
    private static LandedCostApportionment.Line Line(
        decimal quantity,
        decimal valueBase,
        decimal? weight = null) => new()
    {
        Quantity = quantity,
        ValueBase = valueBase,
        UnitWeightGrams = weight,
    };

    private static LandedCostApportionment.Charge Charge(
        decimal amount,
        ChargeApportionMethod method = ChargeApportionMethod.ByValue) => new()
    {
        Amount = amount,
        Method = method,
    };

    [Fact]
    public void Value_apportionment_splits_in_proportion_to_line_value()
    {
        var lines = new[] { Line(10, 1000m), Line(10, 3000m) };

        var total = LandedCostApportionment.Apportion(lines, [Charge(400m)]);

        Assert.Equal(400m, total);
        Assert.Equal(100m, lines[0].ApportionedCharge);
        Assert.Equal(300m, lines[1].ApportionedCharge);
    }

    [Fact]
    public void Quantity_apportionment_splits_per_unit()
    {
        var lines = new[] { Line(2, 5000m), Line(8, 100m) };

        LandedCostApportionment.Apportion(lines, [Charge(500m, ChargeApportionMethod.ByQuantity)]);

        // A per-carton handling fee does not care what is in the carton.
        Assert.Equal(100m, lines[0].ApportionedCharge);
        Assert.Equal(400m, lines[1].ApportionedCharge);
    }

    [Fact]
    public void Weight_apportionment_uses_total_weight_not_unit_weight()
    {
        var lines = new[] { Line(1, 100m, weight: 1000m), Line(10, 100m, weight: 100m) };

        LandedCostApportionment.Apportion(lines, [Charge(200m, ChargeApportionMethod.ByWeight)]);

        // 1kg against 1kg: freight splits evenly even though one line is ten
        // times the units.
        Assert.Equal(100m, lines[0].ApportionedCharge);
        Assert.Equal(100m, lines[1].ApportionedCharge);
    }

    [Fact]
    public void The_shares_always_add_up_to_the_charge_exactly()
    {
        var lines = new[] { Line(1, 100m), Line(1, 100m), Line(1, 100m) };

        var total = LandedCostApportionment.Apportion(lines, [Charge(1000m)]);

        // A third of 1,000 rounds to 333.3333 three times and loses a
        // hundredth. A receipt whose parts do not add up to its total is one
        // nobody can reconcile, so the residual lands somewhere rather than
        // vanishing.
        Assert.Equal(1000m, total);
        Assert.Equal(1000m, lines.Sum(l => l.ApportionedCharge));
    }

    [Fact]
    public void The_rounding_residual_lands_on_the_largest_line()
    {
        // 300 : 300 : 100 of a charge of 100 divides into 42.8571, 42.8571 and
        // 14.2857, which is a hundredth of a taka short of the whole.
        var lines = new[] { Line(1, 300m), Line(1, 300m), Line(1, 100m) };

        LandedCostApportionment.Apportion(lines, [Charge(100m)]);

        Assert.Equal(100m, lines.Sum(l => l.ApportionedCharge));

        // The odd hundredth goes to a largest line, where it distorts the unit
        // cost least - not to whichever happened to be last.
        Assert.Equal(42.8572m, lines[0].ApportionedCharge);
        Assert.Equal(42.8571m, lines[1].ApportionedCharge);
        Assert.Equal(14.2857m, lines[2].ApportionedCharge);
    }

    [Fact]
    public void Several_charges_accumulate_on_each_line()
    {
        var lines = new[] { Line(5, 1000m, weight: 200m), Line(5, 1000m, weight: 200m) };

        var total = LandedCostApportionment.Apportion(
            lines,
            [
                Charge(300m, ChargeApportionMethod.ByValue),
                Charge(200m, ChargeApportionMethod.ByWeight),
                Charge(100m, ChargeApportionMethod.ByQuantity),
            ]);

        Assert.Equal(600m, total);
        Assert.Equal(300m, lines[0].ApportionedCharge);
        Assert.Equal(300m, lines[1].ApportionedCharge);
    }

    [Fact]
    public void Weight_apportionment_falls_back_to_quantity_when_no_weights_are_recorded()
    {
        var lines = new[] { Line(3, 500m), Line(1, 5000m) };

        LandedCostApportionment.Apportion(lines, [Charge(400m, ChargeApportionMethod.ByWeight)]);

        // Nobody has entered weights. Splitting a freight bill evenly across a
        // pallet and a lipstick would be worse than splitting it per unit.
        Assert.Equal(300m, lines[0].ApportionedCharge);
        Assert.Equal(100m, lines[1].ApportionedCharge);
    }

    [Fact]
    public void Value_apportionment_falls_back_to_quantity_on_a_free_shipment()
    {
        var lines = new[] { Line(1, 0m), Line(3, 0m) };

        LandedCostApportionment.Apportion(lines, [Charge(80m)]);

        // Samples and warranty replacements arrive at no cost but still cost
        // something to bring in.
        Assert.Equal(20m, lines[0].ApportionedCharge);
        Assert.Equal(60m, lines[1].ApportionedCharge);
    }

    [Fact]
    public void A_zero_charge_is_ignored_rather_than_divided()
    {
        var lines = new[] { Line(1, 100m) };

        var total = LandedCostApportionment.Apportion(lines, [Charge(0m)]);

        Assert.Equal(0m, total);
        Assert.Equal(0m, lines[0].ApportionedCharge);
    }

    [Fact]
    public void Apportioning_again_replaces_the_previous_result_rather_than_adding_to_it()
    {
        var lines = new[] { Line(1, 100m), Line(1, 100m) };

        LandedCostApportionment.Apportion(lines, [Charge(100m)]);
        LandedCostApportionment.Apportion(lines, [Charge(100m)]);

        // Recosting a receipt twice must not double its freight.
        Assert.Equal(100m, lines.Sum(l => l.ApportionedCharge));
    }

    [Fact]
    public void No_lines_means_nothing_to_apportion()
    {
        var total = LandedCostApportionment.Apportion([], [Charge(500m)]);

        Assert.Equal(0m, total);
    }

    [Fact]
    public void Landed_unit_cost_includes_the_apportioned_charge()
    {
        var cost = LandedCostApportionment.UnitCost(valueBase: 1000m, apportionedCharge: 200m, quantity: 10m);

        Assert.Equal(120m, cost);
    }

    [Fact]
    public void Landed_unit_cost_refuses_a_zero_quantity()
    {
        // Dividing by it would produce infinity and store it as a cost.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LandedCostApportionment.UnitCost(1000m, 0m, 0m));
    }

    [Fact]
    public void A_realistic_import_produces_costs_that_reconcile_to_the_invoice()
    {
        // 100 units at 3 USD and 40 at 12 USD, rate 118, freight 9,000 and duty
        // 14,000 BDT.
        var lines = new[]
        {
            Line(100, 100 * 3m * 118m, weight: 60m),
            Line(40, 40 * 12m * 118m, weight: 250m),
        };

        var total = LandedCostApportionment.Apportion(
            lines,
            [
                Charge(9000m, ChargeApportionMethod.ByWeight),
                Charge(14000m, ChargeApportionMethod.ByValue),
            ]);

        Assert.Equal(23000m, total);
        Assert.Equal(23000m, lines.Sum(l => l.ApportionedCharge));

        var first = LandedCostApportionment.UnitCost(
            lines[0].ValueBase, lines[0].ApportionedCharge, lines[0].Quantity);

        var second = LandedCostApportionment.UnitCost(
            lines[1].ValueBase, lines[1].ApportionedCharge, lines[1].Quantity);

        // Every unit costs more than the goods alone, and the whole shipment
        // reconciles to goods plus charges to the taka.
        Assert.True(first > 354m);
        Assert.True(second > 1416m);

        var reconstructed = (first * 100m) + (second * 40m);
        var expected = lines.Sum(l => l.ValueBase) + 23000m;

        Assert.True(Math.Abs(reconstructed - expected) < 0.02m,
            $"Reconstructed {reconstructed} against {expected}");
    }
}
