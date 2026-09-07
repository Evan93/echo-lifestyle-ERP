using EchoLifestyle.Application.Inventory;

namespace EchoLifestyle.UnitTests.Inventory;

/// <summary>
/// Which batch a sale comes out of.
///
/// Getting this wrong is not a rounding error. Take from the newest batch and
/// the oldest sits there until it expires and is written off - so this decides,
/// quietly and daily, how much stock the business throws away.
/// </summary>
public class FefoAllocationTests
{
    private static FefoAllocation.Candidate Batch(
        long id,
        decimal available,
        string? expiry = null,
        string received = "2026-01-01",
        decimal cost = 100m) => new()
        {
            StockBatchId = id,
            Available = available,
            ExpiryDate = expiry is null ? null : DateOnly.Parse(expiry),
            ReceivedDate = DateOnly.Parse(received),
            LandedUnitCost = cost,
        };

    [Fact]
    public void The_batch_expiring_first_goes_first()
    {
        var result = FefoAllocation.Allocate(
        [
            Batch(1, 10, expiry: "2027-06-01"),
            Batch(2, 10, expiry: "2026-11-01"),
            Batch(3, 10, expiry: "2028-01-01"),
        ],
        5m);

        Assert.True(result.IsComplete);

        var take = Assert.Single(result.Takes);
        Assert.Equal(2, take.StockBatchId);
        Assert.Equal(5m, take.Quantity);
    }

    [Fact]
    public void A_quantity_larger_than_one_batch_spills_into_the_next()
    {
        var result = FefoAllocation.Allocate(
        [
            Batch(1, 3, expiry: "2026-11-01"),
            Batch(2, 10, expiry: "2027-01-01"),
        ],
        8m);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Takes.Count);

        Assert.Equal(1, result.Takes[0].StockBatchId);
        Assert.Equal(3m, result.Takes[0].Quantity);

        Assert.Equal(2, result.Takes[1].StockBatchId);
        Assert.Equal(5m, result.Takes[1].Quantity);
    }

    [Fact]
    public void A_batch_with_no_expiry_waits_behind_every_dated_one()
    {
        // An undated batch has no deadline, so it is not the urgent one - even
        // when it arrived first.
        var result = FefoAllocation.Allocate(
        [
            Batch(1, 10, expiry: null, received: "2025-01-01"),
            Batch(2, 4, expiry: "2029-12-01", received: "2026-08-01"),
        ],
        6m);

        Assert.Equal(2, result.Takes[0].StockBatchId);
        Assert.Equal(4m, result.Takes[0].Quantity);
        Assert.Equal(1, result.Takes[1].StockBatchId);
        Assert.Equal(2m, result.Takes[1].Quantity);
    }

    [Fact]
    public void Undated_batches_fall_back_to_oldest_received()
    {
        var result = FefoAllocation.Allocate(
        [
            Batch(1, 5, expiry: null, received: "2026-05-01"),
            Batch(2, 5, expiry: null, received: "2026-01-01"),
        ],
        3m);

        Assert.Equal(2, Assert.Single(result.Takes).StockBatchId);
    }

    [Fact]
    public void Not_enough_stock_reports_the_shortfall_rather_than_taking_what_it_can()
    {
        var result = FefoAllocation.Allocate([Batch(1, 4, expiry: "2027-01-01")], 10m);

        Assert.False(result.IsComplete);
        Assert.Equal(6m, result.Shortfall);

        // The takes are still there. The caller refuses the whole thing, but a
        // partial allocation is what tells somebody how short they are.
        Assert.Equal(4m, result.Takes.Sum(t => t.Quantity));
    }

    [Fact]
    public void Batches_with_nothing_spare_are_skipped()
    {
        var result = FefoAllocation.Allocate(
        [
            Batch(1, 0, expiry: "2026-10-01"),
            Batch(2, 5, expiry: "2027-01-01"),
        ],
        2m);

        Assert.Equal(2, Assert.Single(result.Takes).StockBatchId);
    }

    [Fact]
    public void Cost_comes_from_the_batches_actually_taken()
    {
        // The whole reason allocation returns takes rather than a number:
        // batches do not share a cost, and a sale spanning two of them cost
        // what those two cost.
        var result = FefoAllocation.Allocate(
        [
            Batch(1, 2, expiry: "2026-11-01", cost: 120m),
            Batch(2, 5, expiry: "2027-01-01", cost: 150m),
        ],
        4m);

        Assert.True(result.IsComplete);

        // 2 x 120 + 2 x 150
        Assert.Equal(540m, result.TotalCost);
    }

    [Fact]
    public void Allocating_nothing_is_a_programming_error_not_a_business_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FefoAllocation.Allocate([Batch(1, 5)], 0m));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => FefoAllocation.Allocate([Batch(1, 5)], -2m));
    }

    [Fact]
    public void An_empty_shelf_is_all_shortfall()
    {
        var result = FefoAllocation.Allocate([], 3m);

        Assert.False(result.IsComplete);
        Assert.Equal(3m, result.Shortfall);
        Assert.Empty(result.Takes);
    }
}
