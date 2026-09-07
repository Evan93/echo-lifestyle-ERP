using EchoLifestyle.Domain.Finance;

namespace EchoLifestyle.Application.Finance.Remittances;

/// <summary>Starting a reconciliation against a courier's statement.</summary>
public class StartRemittanceRequest
{
    public string CourierName { get; set; } = string.Empty;

    public long BranchId { get; set; }

    public DateOnly? RemittanceDate { get; set; }

    public string? StatementReference { get; set; }

    public PaymentMethodKind ReceivedVia { get; set; } = PaymentMethodKind.Bkash;

    public string? Notes { get; set; }
}

/// <summary>
/// The statement, as typed in: which orders it covers and what came back.
/// </summary>
public class SaveRemittanceRequest
{
    public long Id { get; set; }

    public DateOnly? RemittanceDate { get; set; }

    public string? StatementReference { get; set; }

    public PaymentMethodKind ReceivedVia { get; set; }

    public decimal CourierFee { get; set; }

    public decimal OtherDeduction { get; set; }

    /// <summary>What the bank or bKash message actually says arrived.</summary>
    public decimal NetReceived { get; set; }

    public string? Notes { get; set; }

    public IReadOnlyList<RemittanceLineInput> Lines { get; set; } = [];
}

public class RemittanceLineInput
{
    public long SalesOrderId { get; set; }

    public decimal AmountCollected { get; set; }

    public bool IsReturned { get; set; }

    public string? ReturnReason { get; set; }

    public string? Notes { get; set; }
}

public class RemittanceListItem
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public string CourierName { get; set; } = string.Empty;

    public DateOnly RemittanceDate { get; set; }

    public string? StatementReference { get; set; }

    public CourierRemittanceStatus Status { get; set; }

    public int LineCount { get; set; }

    public int ReturnedCount { get; set; }

    public decimal GrossCollected { get; set; }

    public decimal CourierFee { get; set; }

    public decimal NetReceived { get; set; }

    public decimal Discrepancy => NetReceived - (GrossCollected - CourierFee);
}

public class RemittanceDetail
{
    public long Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public string CourierName { get; set; } = string.Empty;

    public long BranchId { get; set; }

    public DateOnly RemittanceDate { get; set; }

    public string? StatementReference { get; set; }

    public CourierRemittanceStatus Status { get; set; }

    public PaymentMethodKind ReceivedVia { get; set; }

    public decimal GrossCollected { get; set; }

    public decimal CourierFee { get; set; }

    public decimal OtherDeduction { get; set; }

    public decimal NetReceived { get; set; }

    public string? Notes { get; set; }

    public DateTime? PostedAtUtc { get; set; }

    public IReadOnlyList<RemittanceLineDetail> Lines { get; set; } = [];

    public decimal ExpectedNet => GrossCollected - CourierFee - OtherDeduction;

    public decimal Discrepancy => NetReceived - ExpectedNet;

    public bool Balances => Discrepancy == 0m;

    public int ReturnedCount => Lines.Count(l => l.IsReturned);

    public int CollectedCount => Lines.Count(l => !l.IsReturned);
}

public class RemittanceLineDetail
{
    public long Id { get; set; }

    public long SalesOrderId { get; set; }

    public string OrderNumber { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string DistrictName { get; set; } = string.Empty;

    public string? ConsignmentNumber { get; set; }

    /// <summary>What the order was for. The figure the collected amount is checked against.</summary>
    public decimal OrderTotal { get; set; }

    /// <summary>Anything already settled against this order before this statement.</summary>
    public decimal PreviouslyCollected { get; set; }

    public decimal AmountCollected { get; set; }

    public bool IsReturned { get; set; }

    public string? ReturnReason { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Collected less than the order asked for. Common enough to be worth
    /// flagging rather than treating as an error - a rider takes what is offered
    /// at the door.
    /// </summary>
    public decimal Shortfall => IsReturned ? 0m : OrderTotal - PreviouslyCollected - AmountCollected;
}

/// <summary>
/// An order with this courier that has not been settled yet - a candidate for
/// the statement being reconciled.
/// </summary>
public class UnsettledOrder
{
    public long SalesOrderId { get; set; }

    public string Number { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string DistrictName { get; set; } = string.Empty;

    public string? ConsignmentNumber { get; set; }

    public DateOnly OrderDate { get; set; }

    public DateTime? DispatchedAtUtc { get; set; }

    public decimal GrandTotal { get; set; }

    public decimal AlreadyCollected { get; set; }

    public decimal Outstanding => GrandTotal - AlreadyCollected;

    /// <summary>
    /// How long the courier has been holding it. The number that turns a vague
    /// worry into a phone call.
    /// </summary>
    public int DaysWithCourier { get; set; }
}

/// <summary>What posting a remittance actually did.</summary>
public class RemittancePostSummary
{
    public string Number { get; set; } = string.Empty;

    public int Settled { get; set; }

    public int Returned { get; set; }

    public decimal Collected { get; set; }

    public decimal Fee { get; set; }

    public decimal Net { get; set; }

    public string Describe()
    {
        var text = $"{Number} posted: {Settled} order(s) settled for {Collected:N2} BDT";

        if (Returned > 0)
        {
            text += $", {Returned} returned to stock";
        }

        return $"{text}. {Net:N2} BDT received after {Fee:N2} in courier charges.";
    }
}
