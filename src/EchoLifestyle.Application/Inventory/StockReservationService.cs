using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Application.Common.Results;
using EchoLifestyle.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Inventory;

/// <summary>
/// Promising stock, and letting go of it again.
///
/// A reservation writes no ledger entry, because nothing has moved. It raises
/// <c>StockBalance.QuantityReserved</c>, which lowers what is available to
/// everybody else while leaving what is on hand alone. That distinction is the
/// whole point: the shelf still holds the jar, but it is spoken for.
///
/// It is also why the balance rebuild deliberately does not touch the reserved
/// column. On-hand is a projection of the ledger and can be recomputed;
/// reservations are commitments against orders that have not shipped, exist
/// nowhere in movement history, and wiping them would quietly release stock
/// somebody has already been promised.
///
/// Planning and applying are separate on purpose. An order reserves every line
/// or none of them, and the only way to be sure of that is to decide the whole
/// thing before touching anything - a half-applied plan abandoned mid-loop
/// leaves modified entities on the context that a later save would flush.
///
/// Nothing here saves or opens a transaction. The caller owns both.
/// </summary>
public class StockReservationService
{
    private readonly IApplicationDbContext _db;

    public StockReservationService(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Works out what every line would take, without changing anything.
    ///
    /// Availability is on hand minus what is already reserved, so two orders
    /// cannot be promised the same unit - the second sees the first one's claim
    /// and is refused. Lines are planned against a running tally, so an order
    /// with the same product on two lines cannot promise itself the same jar
    /// twice either.
    /// </summary>
    public async Task<OperationResult<ReservationPlan>> PlanAsync(
        SalesOrder order,
        CancellationToken cancellationToken = default)
    {
        var plan = new ReservationPlan();

        // Availability is consumed as the plan is built, so a second line for
        // the same product sees what the first one took.
        var pool = new Dictionary<long, List<FefoAllocation.Candidate>>();

        foreach (var line in order.Lines)
        {
            if (!pool.TryGetValue(line.ProductVariantId, out var candidates))
            {
                candidates = await CandidatesAsync(
                    line.ProductVariantId, order.WarehouseId, cancellationToken);

                pool[line.ProductVariantId] = candidates;
            }

            var allocation = FefoAllocation.Allocate(candidates, line.Quantity);

            if (!allocation.IsComplete)
            {
                var available = candidates.Sum(c => c.Available);

                return OperationResult<ReservationPlan>.Failure(
                    $"Only {available:N0} of {line.Sku} can be promised - this order needs "
                    + $"{line.Quantity:N0}. Somebody else's confirmed order may already be holding it.");
            }

            foreach (var take in allocation.Takes)
            {
                plan.Takes.Add(new PlannedReservation
                {
                    SalesOrderLineId = line.Id,
                    ProductVariantId = line.ProductVariantId,
                    StockBatchId = take.StockBatchId,
                    Quantity = take.Quantity,
                });
            }

            pool[line.ProductVariantId] = Consume(candidates, allocation);
        }

        return OperationResult<ReservationPlan>.Success(plan);
    }

    /// <summary>
    /// Applies a plan that has already been found sound. Only reached when
    /// every line can be filled.
    /// </summary>
    public async Task ApplyAsync(
        SalesOrder order,
        ReservationPlan plan,
        CancellationToken cancellationToken = default)
    {
        foreach (var take in plan.Takes)
        {
            var balance = await _db.StockBalances.FirstAsync(
                b => b.ProductVariantId == take.ProductVariantId
                     && b.WarehouseId == order.WarehouseId
                     && b.StockBatchId == take.StockBatchId,
                cancellationToken);

            balance.QuantityReserved += take.Quantity;

            order.Reservations.Add(new StockReservation
            {
                SalesOrderId = order.Id,
                SalesOrderLineId = take.SalesOrderLineId,
                ProductVariantId = take.ProductVariantId,
                WarehouseId = order.WarehouseId,
                StockBatchId = take.StockBatchId,
                Quantity = take.Quantity,
            });
        }
    }

    /// <summary>
    /// Gives every reservation on an order back.
    ///
    /// Puts each quantity back on the batch it was taken from, which is the
    /// reason reservations are held per batch rather than per product. Called on
    /// cancellation, and again at dispatch once the stock is about to leave for
    /// real.
    /// </summary>
    public async Task ReleaseAllAsync(
        SalesOrder order,
        CancellationToken cancellationToken = default)
    {
        var reservations = order.Reservations.ToList();

        if (reservations.Count == 0)
        {
            return;
        }

        var batchIds = reservations.Select(r => r.StockBatchId).Distinct().ToList();

        var balances = await _db.StockBalances
            .Where(b => b.WarehouseId == order.WarehouseId && batchIds.Contains(b.StockBatchId))
            .ToListAsync(cancellationToken);

        foreach (var reservation in reservations)
        {
            var balance = balances.FirstOrDefault(
                b => b.StockBatchId == reservation.StockBatchId
                     && b.ProductVariantId == reservation.ProductVariantId);

            if (balance is null)
            {
                // The balance row is gone - only possible if somebody deleted it
                // directly. Nothing to give back; a rebuild will recreate the row
                // with reserved at zero, which is the right answer.
                continue;
            }

            // Clamped at zero. A reservation released twice would otherwise
            // drive the column negative, and the check constraint would fail a
            // save that has nothing wrong with it.
            balance.QuantityReserved = Math.Max(0m, balance.QuantityReserved - reservation.Quantity);
        }

        _db.StockReservations.RemoveRange(reservations);
        order.Reservations.Clear();
    }

    /// <summary>
    /// What could be promised right now for one product in one warehouse.
    ///
    /// On hand minus reserved, which is the only number a salesperson should
    /// ever be shown. On hand alone includes stock already sold to somebody
    /// waiting for a courier.
    /// </summary>
    public async Task<decimal> AvailableAsync(
        long productVariantId,
        long warehouseId,
        CancellationToken cancellationToken = default)
    {
        var candidates = await CandidatesAsync(productVariantId, warehouseId, cancellationToken);

        return candidates.Sum(c => c.Available);
    }

    /// <summary>
    /// Every batch of one product with something spare in it, ready for the
    /// allocator.
    /// </summary>
    /// <param name="ignoringOrderId">
    /// Treat this order's own reservations as free. Used at dispatch, where the
    /// order is about to release what it is holding and must not be told it is
    /// competing with itself.
    /// </param>
    public async Task<List<FefoAllocation.Candidate>> CandidatesAsync(
        long productVariantId,
        long warehouseId,
        CancellationToken cancellationToken = default,
        long? ignoringOrderId = null)
    {
        var own = ignoringOrderId is null
            ? []
            : await _db.StockReservations
                .AsNoTracking()
                .Where(r => r.SalesOrderId == ignoringOrderId
                            && r.ProductVariantId == productVariantId
                            && r.WarehouseId == warehouseId)
                .GroupBy(r => r.StockBatchId)
                .Select(g => new { StockBatchId = g.Key, Quantity = g.Sum(r => r.Quantity) })
                .ToDictionaryAsync(g => g.StockBatchId, g => g.Quantity, cancellationToken);

        var rows = await _db.StockBalances
            .AsNoTracking()
            .Where(b => b.ProductVariantId == productVariantId && b.WarehouseId == warehouseId)
            .Select(b => new
            {
                b.StockBatchId,
                b.QuantityOnHand,
                b.QuantityReserved,
                ExpiryDate = b.StockBatch!.ExpiryDate,
                b.StockBatch.ReceivedDate,
                b.StockBatch.LandedUnitCost,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(b => new FefoAllocation.Candidate
            {
                StockBatchId = b.StockBatchId,
                Available = b.QuantityOnHand
                            - b.QuantityReserved
                            + own.GetValueOrDefault(b.StockBatchId, 0m),
                ExpiryDate = b.ExpiryDate,
                ReceivedDate = b.ReceivedDate,
                LandedUnitCost = b.LandedUnitCost,
            })
            .Where(c => c.Available > 0m)
            .ToList();
    }

    /// <summary>
    /// The same candidates with an allocation's takes deducted, so the next line
    /// of the same order sees what this one claimed.
    /// </summary>
    private static List<FefoAllocation.Candidate> Consume(
        List<FefoAllocation.Candidate> candidates,
        FefoAllocation.Result allocation)
    {
        var taken = allocation.Takes
            .GroupBy(t => t.StockBatchId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Quantity));

        return candidates
            .Select(c => new FefoAllocation.Candidate
            {
                StockBatchId = c.StockBatchId,
                Available = c.Available - taken.GetValueOrDefault(c.StockBatchId, 0m),
                ExpiryDate = c.ExpiryDate,
                ReceivedDate = c.ReceivedDate,
                LandedUnitCost = c.LandedUnitCost,
            })
            .Where(c => c.Available > 0m)
            .ToList();
    }
}

/// <summary>What an order would reserve, decided before anything is touched.</summary>
public class ReservationPlan
{
    public List<PlannedReservation> Takes { get; } = [];
}

public class PlannedReservation
{
    public required long SalesOrderLineId { get; init; }

    public required long ProductVariantId { get; init; }

    public required long StockBatchId { get; init; }

    public required decimal Quantity { get; init; }
}
