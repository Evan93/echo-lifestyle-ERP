using System.ComponentModel.DataAnnotations;
using EchoLifestyle.Application.Finance.Remittances;
using EchoLifestyle.Domain.Finance;
using EchoLifestyle.Web.Areas.BackOffice.Models.Purchasing;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Finance;

public class StartRemittanceFormModel
{
    [Required(ErrorMessage = "Which courier is this from?")]
    [StringLength(100)]
    [Display(Name = "Courier")]
    public string CourierName { get; set; } = string.Empty;

    [Display(Name = "Branch")]
    public long BranchId { get; set; }

    [Display(Name = "Money arrived on")]
    [DataType(DataType.Date)]
    public DateOnly? RemittanceDate { get; set; }

    [StringLength(100)]
    [Display(Name = "Statement reference")]
    public string? StatementReference { get; set; }

    [Display(Name = "Received via")]
    public PaymentMethodKind ReceivedVia { get; set; } = PaymentMethodKind.Bkash;

    [StringLength(1000)]
    [Display(Name = "Notes")]
    public string? Notes { get; set; }

    public IReadOnlyList<BranchOption> Branches { get; set; } = [];

    /// <summary>Couriers this business has actually shipped with.</summary>
    public IReadOnlyList<string> KnownCouriers { get; set; } = [];

    public StartRemittanceRequest ToRequest() => new()
    {
        CourierName = CourierName,
        BranchId = BranchId,
        RemittanceDate = RemittanceDate,
        StatementReference = StatementReference,
        ReceivedVia = ReceivedVia,
        Notes = Notes,
    };
}

/// <summary>
/// The statement being ticked off.
///
/// Every candidate order is posted back, selected or not, so unticking one
/// removes it. A form that only sends the ticked rows cannot express "I made a
/// mistake, take that one off".
/// </summary>
public class ReconcileFormModel
{
    public long Id { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? RemittanceDate { get; set; }

    [StringLength(100)]
    public string? StatementReference { get; set; }

    public PaymentMethodKind ReceivedVia { get; set; }

    [Range(0, 99999999)]
    [Display(Name = "Courier charges")]
    public decimal CourierFee { get; set; }

    [Range(0, 99999999)]
    [Display(Name = "Other deductions")]
    public decimal OtherDeduction { get; set; }

    [Range(0, 99999999)]
    [Display(Name = "Amount received")]
    public decimal NetReceived { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    /// <summary>
    /// Post even though the figures do not agree. Deliberately a separate tick:
    /// a payout that does not add up should stop somebody, and recording the
    /// difference should be a decision rather than a default.
    /// </summary>
    public bool AcceptDiscrepancy { get; set; }

    public List<ReconcileRow> Rows { get; set; } = [];

    public SaveRemittanceRequest ToRequest() => new()
    {
        Id = Id,
        RemittanceDate = RemittanceDate,
        StatementReference = StatementReference,
        ReceivedVia = ReceivedVia,
        CourierFee = CourierFee,
        OtherDeduction = OtherDeduction,
        NetReceived = NetReceived,
        Notes = Notes,
        Lines = Rows
            .Where(r => r.IsOnStatement)
            .Select(r => new RemittanceLineInput
            {
                SalesOrderId = r.SalesOrderId,
                AmountCollected = r.IsReturned ? 0m : r.AmountCollected,
                IsReturned = r.IsReturned,
                ReturnReason = r.ReturnReason,
                Notes = r.Notes,
            })
            .ToList(),
    };
}

public class ReconcileRow
{
    public long SalesOrderId { get; set; }

    /// <summary>Ticked means the courier's statement mentions this order.</summary>
    public bool IsOnStatement { get; set; }

    [Range(0, 99999999)]
    public decimal AmountCollected { get; set; }

    public bool IsReturned { get; set; }

    [StringLength(500)]
    public string? ReturnReason { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
