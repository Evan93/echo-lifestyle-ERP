using EchoLifestyle.Domain.Administration;
using EchoLifestyle.Domain.Catalog;
using EchoLifestyle.Domain.Common;

namespace EchoLifestyle.Domain.Inventory;

/// <summary>
/// How much of one batch sits in one warehouse right now.
///
/// A projection, not a fact: every row here is the sum of the ledger entries
/// behind it and can be rebuilt from them. It exists because asking "can I sell
/// this?" must not mean summing a ledger that grows forever.
///
/// Nothing outside the inventory service writes to it, and nothing writes to it
/// without writing the ledger entry that justifies the change in the same
/// transaction.
/// </summary>
public class StockBalance : BaseEntity
{
    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public long WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    public long StockBatchId { get; set; }

    public StockBatch? StockBatch { get; set; }

    public decimal QuantityOnHand { get; set; }

    /// <summary>
    /// Committed to orders that have not shipped. Held separately so that two
    /// customers cannot be sold the last unit while one of them is still in
    /// checkout.
    /// </summary>
    public decimal QuantityReserved { get; set; }

    /// <summary>
    /// What can actually be promised. Not stored - a stored copy is one more
    /// thing that can disagree with the two numbers it comes from.
    /// </summary>
    public decimal QuantityAvailable => QuantityOnHand - QuantityReserved;

    public DateTime? LastMovementAtUtc { get; set; }
}
