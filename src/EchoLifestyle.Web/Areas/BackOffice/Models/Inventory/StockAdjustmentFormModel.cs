using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Inventory.Adjustments;
using EchoLifestyle.Application.Purchasing.Receiving;
using EchoLifestyle.Domain.Inventory;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Inventory;

/// <summary>
/// One screen of adjusting.
///
/// Lines are appended by the search box, the same way a purchase is entered -
/// with one extra step, because an adjustment has to name a batch. Nothing here
/// is trusted; the service revalidates every field.
/// </summary>
public class StockAdjustmentFormModel
{
    [Required(ErrorMessage = "Choose a warehouse.")]
    [Display(Name = "Warehouse")]
    public long WarehouseId { get; set; }

    [Display(Name = "Branch")]
    public long BranchId { get; set; }

    [Display(Name = "Happened on")]
    [DataType(DataType.Date)]
    public DateOnly? AdjustmentDate { get; set; }

    [Display(Name = "Reason")]
    public StockAdjustmentReason Reason { get; set; } = StockAdjustmentReason.Damaged;

    [Required(ErrorMessage = "Explain what happened - this document has no invoice behind it.")]
    [StringLength(1000, MinimumLength = 5,
        ErrorMessage = "Say a little more than that; the note is the whole record.")]
    [Display(Name = "What happened")]
    public string ReasonNotes { get; set; } = string.Empty;

    public List<AdjustmentLineRow> Lines { get; set; } = [];

    public IReadOnlyList<WarehouseOption> Warehouses { get; set; } = [];

    public IReadOnlyList<BranchOption> Branches { get; set; } = [];

    /// <summary>
    /// Whether the acting user can approve. Changes the button from "Submit for
    /// approval" to "Post", and the warning underneath it.
    /// </summary>
    public bool CanApprove { get; set; }

    public SubmitAdjustmentRequest ToRequest() => new()
    {
        WarehouseId = WarehouseId,
        BranchId = BranchId,
        AdjustmentDate = AdjustmentDate,
        Reason = Reason,
        ReasonNotes = ReasonNotes,
        Lines = Lines
            .Where(l => l.ProductVariantId > 0 && l.StockBatchId > 0)
            .Select(l => new AdjustmentLineInput
            {
                ProductVariantId = l.ProductVariantId,
                StockBatchId = l.StockBatchId,

                // The form collects a plain positive number and the reason
                // decides the direction. Asking somebody to type a minus sign
                // for "damaged" is asking for the one mistake that doubles a
                // write-off.
                QuantityChange = StockAdjustment.IsInboundOnly(Reason)
                    ? Math.Abs(l.Quantity)
                    : Reason == StockAdjustmentReason.Correction
                        ? l.Quantity
                        : -Math.Abs(l.Quantity),
                Notes = l.Notes,
            })
            .ToList(),
    };
}

public class AdjustmentLineRow
{
    public long ProductVariantId { get; set; }

    public long StockBatchId { get; set; }

    /// <summary>Posted back so a failed save redraws the line without another lookup.</summary>
    public string Sku { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string BatchLabel { get; set; } = string.Empty;

    /// <summary>
    /// Signed only for Correction, where either direction is meaningful.
    /// Everywhere else the reason supplies the sign.
    /// </summary>
    [Range(-9999999, 9999999)]
    public decimal Quantity { get; set; } = 1m;

    [StringLength(500)]
    public string? Notes { get; set; }
}

/// <summary>Rejecting a pending adjustment. A reason is not optional.</summary>
public class RejectAdjustmentModel
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Say why it is being turned down.")]
    [StringLength(1000, MinimumLength = 3)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;
}
