using EchoLifestyle.Application.Common.Interfaces;
using EchoLifestyle.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EchoLifestyle.Application.Inventory;

/// <summary>
/// The one place that appends to the stock ledger and moves the balance that
/// mirrors it.
///
/// Rule 1 of this system is that stock never moves without a ledger entry. That
/// rule is only as good as the number of places that can break it, so every
/// document type after receiving posts through here rather than writing the two
/// tables itself. The entry and the balance change are added to the same change
/// tracker in the same call - they cannot be separated by a later edit.
///
/// Nothing here saves or opens a transaction. The caller owns both, because a
/// document posts all of its lines or none of them.
/// </summary>
public class StockMovementWriter
{
    private readonly IApplicationDbContext _db;

    public StockMovementWriter(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Appends one movement and applies it to the batch's balance, creating the
    /// balance row if this batch has never been in this warehouse before.
    /// </summary>
    /// <returns>The balance row after the movement, so callers can inspect it.</returns>
    public async Task<StockBalance> AppendAsync(
        StockMovement movement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);

        if (movement.QuantityChange == 0m)
        {
            // The ledger has a check constraint saying the same thing. Failing
            // here instead gives a caller a stack trace pointing at the bug
            // rather than a SQL error pointing at the symptom.
            throw new ArgumentException(
                "A movement of zero is not a movement.", nameof(movement));
        }

        var valueChange = decimal.Round(
            movement.QuantityChange * movement.UnitCost, 4, MidpointRounding.AwayFromZero);

        _db.StockLedger.Add(new StockLedgerEntry
        {
            ProductVariantId = movement.ProductVariantId,
            WarehouseId = movement.WarehouseId,
            StockBatchId = movement.StockBatchId,
            MovementType = movement.MovementType,
            QuantityChange = movement.QuantityChange,
            UnitCost = movement.UnitCost,
            ValueChange = valueChange,
            BranchId = movement.BranchId,
            DocumentType = movement.DocumentType,
            DocumentId = movement.DocumentId,
            DocumentNumber = movement.DocumentNumber,
            OccurredAtUtc = movement.OccurredAtUtc,
            BusinessDate = movement.BusinessDate,
            Notes = movement.Notes,
        });

        var balance = await _db.StockBalances.FirstOrDefaultAsync(
            b => b.ProductVariantId == movement.ProductVariantId
                 && b.WarehouseId == movement.WarehouseId
                 && b.StockBatchId == movement.StockBatchId,
            cancellationToken);

        if (balance is null)
        {
            balance = new StockBalance
            {
                ProductVariantId = movement.ProductVariantId,
                WarehouseId = movement.WarehouseId,
                StockBatchId = movement.StockBatchId,
                QuantityOnHand = movement.QuantityChange,
                QuantityReserved = 0m,
                LastMovementAtUtc = movement.OccurredAtUtc,
            };

            _db.StockBalances.Add(balance);
        }
        else
        {
            balance.QuantityOnHand += movement.QuantityChange;
            balance.LastMovementAtUtc = movement.OccurredAtUtc;
        }

        return balance;
    }
}

/// <summary>One movement, described in full. Every field is required to mean something.</summary>
public class StockMovement
{
    public required long ProductVariantId { get; init; }

    public required long WarehouseId { get; init; }

    public required long StockBatchId { get; init; }

    public required StockMovementType MovementType { get; init; }

    /// <summary>Signed. The database enforces that the sign matches the type.</summary>
    public required decimal QuantityChange { get; init; }

    /// <summary>
    /// The batch's landed cost at the moment of the movement. Frozen on the
    /// entry, so recosting a batch later cannot rewrite what past movements were
    /// worth.
    /// </summary>
    public required decimal UnitCost { get; init; }

    public long? BranchId { get; init; }

    public required StockDocumentType DocumentType { get; init; }

    public required long DocumentId { get; init; }

    public required string DocumentNumber { get; init; }

    public required DateTime OccurredAtUtc { get; init; }

    public required DateOnly BusinessDate { get; init; }

    public string? Notes { get; init; }
}
