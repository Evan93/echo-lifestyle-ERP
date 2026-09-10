namespace EchoLifestyle.Application.Reporting;

/// <summary>
/// One day of trading.
///
/// Grouped by the order's business date, which is already Asia/Dhaka - never by
/// the raw UTC timestamp. An order taken at 11pm Dhaka is 5pm UTC the same day,
/// but one taken at 7am Dhaka is 1am UTC, and grouping on UTC would quietly
/// move half of every morning's sales into the previous day.
/// </summary>
public class SalesDayRow
{
    public DateOnly Date { get; set; }

    /// <summary>Orders that shipped and stayed shipped. See <see cref="SalesSummary"/>.</summary>
    public int Orders { get; set; }

    public decimal Units { get; set; }

    /// <summary>Goods only, after any discount. Delivery is not a sale.</summary>
    public decimal Sales { get; set; }

    public decimal DeliveryCharged { get; set; }

    public decimal Total { get; set; }

    /// <summary>
    /// What the goods cost, summed from the batches actually issued. Real, not
    /// an average: two jars of the same cream bought at different prices leave
    /// the shelf at what each of them cost.
    /// </summary>
    public decimal Cost { get; set; }

    public decimal Margin => Sales - Cost;

    public decimal MarginPercent => Sales == 0m ? 0m : Math.Round(Margin / Sales * 100m, 1);

    /// <summary>Orders placed on this date that later came back.</summary>
    public int ReturnedOrders { get; set; }

    public decimal ReturnedValue { get; set; }
}

/// <summary>
/// A period of trading, and what "sold" was taken to mean.
///
/// <para>
/// Only orders that reached the courier count. Draft, confirmed and packed
/// orders are promises, not sales - counting them would report revenue that
/// can still evaporate with a phone call, which in a cash-on-delivery business
/// happens daily.
/// </para>
/// <para>
/// Returned orders are excluded from the sales figures and reported separately
/// rather than hidden. A return is not a sale that did not happen; it is a sale
/// that happened and then unhappened, and it costs real delivery money both
/// ways. The rate is the number a COD business has to watch.
/// </para>
/// </summary>
public class SalesSummary
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public IReadOnlyList<SalesDayRow> Days { get; set; } = [];

    public int Orders => Days.Sum(d => d.Orders);

    public decimal Units => Days.Sum(d => d.Units);

    public decimal Sales => Days.Sum(d => d.Sales);

    public decimal DeliveryCharged => Days.Sum(d => d.DeliveryCharged);

    public decimal Total => Days.Sum(d => d.Total);

    public decimal Cost => Days.Sum(d => d.Cost);

    public decimal Margin => Sales - Cost;

    public decimal MarginPercent => Sales == 0m ? 0m : Math.Round(Margin / Sales * 100m, 1);

    public int ReturnedOrders => Days.Sum(d => d.ReturnedOrders);

    public decimal ReturnedValue => Days.Sum(d => d.ReturnedValue);

    /// <summary>
    /// Returns as a share of everything that went out. The figure that decides
    /// whether cash on delivery is working: a return costs the delivery fee
    /// twice and returns the goods in unknown condition, so a rate creeping
    /// past the low teens is eating the margin above it.
    /// </summary>
    public decimal ReturnRatePercent
    {
        get
        {
            var shipped = Orders + ReturnedOrders;

            return shipped == 0 ? 0m : Math.Round(ReturnedOrders / (decimal)shipped * 100m, 1);
        }
    }

    public decimal AverageOrderValue =>
        Orders == 0 ? 0m : Math.Round(Total / Orders, 2);

    /// <summary>Orders taken but not yet shipped. Not revenue, but worth seeing.</summary>
    public int PipelineOrders { get; set; }

    public decimal PipelineValue { get; set; }
}

/// <summary>
/// One parcel whose money has not come back yet.
///
/// The most-read report in a cash-on-delivery business. Goods leave days before
/// the money returns, so at any moment a meaningful slice of the month's
/// revenue is sitting with a courier - and the only way to notice that a
/// remittance was short, late or never sent is to look at how long each parcel
/// has been out.
/// </summary>
public class OutstandingOrderRow
{
    public long OrderId { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateOnly OrderDate { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string DistrictName { get; set; } = string.Empty;

    public string? CourierName { get; set; }

    public string? ConsignmentNumber { get; set; }

    public decimal GrandTotal { get; set; }

    public decimal AmountCollected { get; set; }

    public decimal Outstanding => GrandTotal - AmountCollected;

    public DateTime? DispatchedAtUtc { get; set; }

    /// <summary>Days since the parcel left. Null before dispatch, which cannot happen here.</summary>
    public int DaysOut { get; set; }

    /// <summary>
    /// Part paid. Usually a courier remitting net of its own fee, which is a
    /// reconciliation to do rather than a debt to chase - so it is worth being
    /// able to tell the two apart at a glance.
    /// </summary>
    public bool IsPartlyPaid => AmountCollected > 0m && Outstanding > 0m;
}

/// <summary>Money still with couriers, and how long it has been there.</summary>
public class OutstandingSummary
{
    public IReadOnlyList<OutstandingOrderRow> Rows { get; set; } = [];

    public decimal Total => Rows.Sum(r => r.Outstanding);

    public int Count => Rows.Count;

    /// <summary>
    /// Aged in the buckets a courier's own cycle suggests. Steadfast remits
    /// roughly weekly, so anything past a fortnight has missed a cycle and
    /// anything past a month needs a phone call rather than patience.
    /// </summary>
    public decimal WithinWeek => Rows.Where(r => r.DaysOut <= 7).Sum(r => r.Outstanding);

    public decimal OneToTwoWeeks =>
        Rows.Where(r => r.DaysOut is > 7 and <= 14).Sum(r => r.Outstanding);

    public decimal TwoToFourWeeks =>
        Rows.Where(r => r.DaysOut is > 14 and <= 30).Sum(r => r.Outstanding);

    public decimal OverAMonth => Rows.Where(r => r.DaysOut > 30).Sum(r => r.Outstanding);
}

/// <summary>What actually sells.</summary>
public class TopProductRow
{
    /// <summary>
    /// The name as it was on the order line, not the product's name today.
    /// A product renamed last month did not sell under its new name, and a
    /// report that says otherwise cannot be reconciled with the invoices.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public decimal Units { get; set; }

    public decimal Sales { get; set; }

    public decimal Cost { get; set; }

    public decimal Margin => Sales - Cost;

    public int Orders { get; set; }
}

/// <summary>One line of the stock valuation.</summary>
public class StockValueRow
{
    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string BrandName { get; set; } = string.Empty;

    public decimal QuantityOnHand { get; set; }

    public decimal QuantityReserved { get; set; }

    public decimal QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>
    /// At what it cost, never at what it sells for. Valuing stock at retail
    /// books a profit that has not been earned and will not be earned on
    /// anything that expires or is written off.
    /// </summary>
    public decimal Value { get; set; }

    public bool ShowVariantName =>
        !string.IsNullOrWhiteSpace(VariantName)
        && !string.Equals(VariantName, ProductName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Something running out, or about to.</summary>
public class LowStockRow
{
    public long ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public decimal Available { get; set; }

    /// <summary>
    /// Whether the website is currently offering it. A published product at
    /// zero is worse than an unpublished one: somebody is being shown a thing
    /// that cannot be bought.
    /// </summary>
    public bool IsPublished { get; set; }

    public bool ShowVariantName =>
        !string.IsNullOrWhiteSpace(VariantName)
        && !string.Equals(VariantName, ProductName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What the shelves hold and what it is worth.</summary>
public class StockReport
{
    public IReadOnlyList<StockValueRow> Rows { get; set; } = [];

    public IReadOnlyList<LowStockRow> LowStock { get; set; } = [];

    /// <summary>
    /// Borrowed wholesale from the inventory module rather than reimplemented.
    ///
    /// Inventory already has a Near-expiry screen with its own query, and a
    /// second one here would be a second definition of "expiring" - two screens
    /// that can quietly disagree about the same shelf. This report asks that
    /// service the same question the other screen asks, so the two can never
    /// give different answers.
    /// </summary>
    public IReadOnlyList<Inventory.ExpiringBatchItem> Expiring { get; set; } = [];

    public decimal TotalValue => Rows.Sum(r => r.Value);

    public decimal TotalUnits => Rows.Sum(r => r.QuantityOnHand);

    /// <summary>Value of everything already past its date. Money already lost.</summary>
    public decimal ExpiredValue => Expiring.Where(e => e.HasExpired).Sum(e => e.ValueAtRisk);
}

/// <summary>
/// One kind of money leaving, over a period.
///
/// Deliberately by <em>kind</em> rather than lumped into one "expenses" figure.
/// Paying a supplier, losing a slice of a payout to a courier, and buying
/// Facebook advertising are three different decisions with three different
/// levers, and a single total hides which one is growing.
/// </summary>
public class SpendKindRow
{
    public string Name { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public int Entries { get; set; }

    /// <summary>
    /// True for the costs that rise with every parcel - courier fees,
    /// packaging, payment charges. Separating them from the cost of simply
    /// being open is what makes a per-order margin mean anything.
    /// </summary>
    public bool IsCostOfSale { get; set; }

    public decimal ShareOfTotal { get; set; }
}

/// <summary>One supplier's worth of buying over a period.</summary>
public class PurchaseSupplierRow
{
    public long SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public int Receipts { get; set; }

    /// <summary>The goods themselves, before freight and clearing.</summary>
    public decimal Goods { get; set; }

    /// <summary>
    /// Freight, clearing, carrying - whatever was apportioned onto the stock.
    /// Worth its own column: it is the part of a purchase that is easy to
    /// forget when comparing one supplier's prices with another's.
    /// </summary>
    public decimal Charges { get; set; }

    public decimal Total => Goods + Charges;
}

/// <summary>
/// Everything that goes out, and what was bought.
///
/// <para>
/// The two halves are reported separately and never added together, because
/// they answer different questions and adding them double-counts. <em>Cash
/// out</em> is money that has actually left. <em>Purchases</em> is stock that
/// has arrived, whether or not it has been paid for yet.
/// </para>
/// <para>
/// Buying ৳50,000 of stock on credit is a purchase with no cash out. Paying
/// last month's supplier bill is cash out with no purchase. Treating the two as
/// one number is the most common way a small business convinces itself it is
/// losing money in a month when it is not, or the reverse.
/// </para>
/// </summary>
public class SpendReport
{
    public DateOnly From { get; set; }

    public DateOnly To { get; set; }

    public IReadOnlyList<SpendKindRow> CashOut { get; set; } = [];

    /// <summary>
    /// The expense kind opened up by category, so "Advertising" is a line you
    /// can read rather than a slice of a total. Comes from the finance module's
    /// own breakdown rather than a second query of the same rows.
    /// </summary>
    public IReadOnlyList<Finance.Cash.ExpenseBreakdownRow> Expenses { get; set; } = [];

    public IReadOnlyList<PurchaseSupplierRow> Purchases { get; set; } = [];

    public decimal TotalCashOut => CashOut.Sum(r => r.Amount);

    public decimal CostOfSale => CashOut.Where(r => r.IsCostOfSale).Sum(r => r.Amount);

    public decimal Operating => TotalCashOut - CostOfSale;

    public decimal TotalPurchased => Purchases.Sum(r => r.Total);

    public int PurchaseReceipts => Purchases.Sum(r => r.Receipts);
}
