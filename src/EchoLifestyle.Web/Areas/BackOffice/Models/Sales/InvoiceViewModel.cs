using EchoLifestyle.Application.Administration.CompanyProfile;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Sales;

namespace EchoLifestyle.Web.Areas.BackOffice.Models.Sales;

/// <summary>
/// The sheet that goes in the parcel.
///
/// Its own model, copied out of the order rather than wrapping it, and that is
/// the whole point. <see cref="SalesOrderDetail"/> carries cost of goods and
/// margin on every line; this document is opened by a customer and read by a
/// courier. Nothing here can show them, because nothing here holds them.
///
/// Hiding those fields with a print stylesheet would have been quicker and
/// wrong: the numbers would still have been in the page source, one "view
/// source" or one careless later edit away from a customer seeing what Echo
/// pays for a bottle of shampoo.
/// </summary>
public class InvoiceViewModel
{
    public required long Id { get; init; }

    public required string Number { get; init; }

    public required DateOnly OrderDate { get; init; }

    public required SalesOrderStatus Status { get; init; }

    public required PaymentMethod PaymentMethod { get; init; }

    public required string RecipientName { get; init; }

    public required string RecipientPhone { get; init; }

    /// <summary>The delivery address as a courier reads it, plus the district.</summary>
    public required string DeliveryAddress { get; init; }

    public string? DeliveryNotes { get; init; }

    public string? CourierName { get; init; }

    public string? ConsignmentNumber { get; init; }

    public required IReadOnlyList<InvoiceLine> Lines { get; init; }

    public required decimal SubTotal { get; init; }

    public required decimal DiscountAmount { get; init; }

    public required decimal DeliveryCharge { get; init; }

    public required decimal GrandTotal { get; init; }

    public required decimal AmountCollected { get; init; }

    /// <summary>
    /// What is still owed, and the one invariant of the box on the sheet: the
    /// large number there is always this, never the order total and never what
    /// has already been paid.
    ///
    /// The first version printed the amount already paid in that box on a
    /// settled order, which is the exact shape of the mistake the box exists to
    /// prevent - a courier reading a large figure in the collect slot and
    /// asking for money that has already changed hands.
    ///
    /// Floored at zero. An over-collection - a customer rounding a cash payment
    /// up, say - is a refund to sort out, not a negative number to print on the
    /// sheet in their hand.
    /// </summary>
    public decimal AmountToCollect => Math.Max(0m, GrandTotal - AmountCollected);

    /// <summary>Nothing left to pay. Zero is printed, not hidden.</summary>
    public bool IsSettled => AmountCollected >= GrandTotal;

    /// <summary>
    /// A draft has reserved nothing. Somebody who prints one and packs from it
    /// is picking stock the system still believes is available, so the sheet
    /// says so in large letters rather than looking like every other invoice.
    /// </summary>
    public bool IsDraft => Status == SalesOrderStatus.Draft;

    /// <summary>
    /// What the box is called. Cash on delivery is the ordinary case and says
    /// so plainly; a part-paid order says "balance" so nobody reads the figure
    /// as the whole order.
    /// </summary>
    public string CollectLabel => IsSettled
        ? "Nothing to collect"
        : PaymentMethod == PaymentMethod.CashOnDelivery
            ? "Cash to collect"
            : "Balance to collect";

    /// <summary>The courier reference, when there is one, as one readable line.</summary>
    public string? CourierReference => string.IsNullOrWhiteSpace(ConsignmentNumber)
        ? null
        : string.Join(" · ", new[] { CourierName, ConsignmentNumber }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    // -----------------------------------------------------------------------
    // The company, as it appears on the letterhead
    // -----------------------------------------------------------------------

    public required string CompanyName { get; init; }

    public string? CompanyPhone { get; init; }

    public string? CompanyEmail { get; init; }

    /// <summary>
    /// Printed only once there is a registration number to print. Until NBR
    /// registration this is blank and the document simply does not mention VAT,
    /// which is the truthful thing for it to do.
    /// </summary>
    public string? VatRegistrationNumber { get; init; }

    public required IReadOnlyList<string> CompanyAddressLines { get; init; }

    /// <summary>
    /// Opens the browser's print dialog on load. Set when the Print button was
    /// pressed, unset when somebody is just looking — the same document either
    /// way, so the URL can be shared without firing a dialog at whoever opens it.
    /// </summary>
    public bool AutoPrint { get; init; }

    // -----------------------------------------------------------------------

    public static InvoiceViewModel From(
        SalesOrderDetail order,
        CompanyDetail company,
        bool autoPrint)
    {
        var address = new List<string>();

        AddAddress(company.AddressLine1);
        AddAddress(company.AddressLine2);
        AddAddress(string.Join(" ", new[] { company.City, company.PostalCode }
            .Where(part => !string.IsNullOrWhiteSpace(part))));

        return new InvoiceViewModel
        {
            Id = order.Id,
            Number = order.Number,
            OrderDate = order.OrderDate,
            Status = order.Status,
            PaymentMethod = order.PaymentMethod,

            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhoneDisplay,
            DeliveryAddress = order.DeliveryOneLine,
            DeliveryNotes = order.DeliveryNotes,
            CourierName = order.CourierName,
            ConsignmentNumber = order.ConsignmentNumber,

            Lines = order.Lines
                .Select(line => new InvoiceLine
                {
                    Sku = line.Sku,
                    ProductName = line.ProductName,
                    VariantName = line.VariantName,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    DiscountAmount = line.DiscountAmount,
                    LineTotal = line.LineTotal,
                })
                .ToList(),

            SubTotal = order.SubTotal,
            DiscountAmount = order.DiscountAmount,
            DeliveryCharge = order.DeliveryCharge,
            GrandTotal = order.GrandTotal,
            AmountCollected = order.AmountCollected,

            CompanyName = company.Name,
            CompanyPhone = company.Phone,
            CompanyEmail = company.Email,
            VatRegistrationNumber = company.VatRegistrationNumber,
            CompanyAddressLines = address,

            AutoPrint = autoPrint,
        };

        void AddAddress(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                address.Add(value.Trim());
            }
        }
    }
}

/// <summary>One line as the customer sees it. No cost, no margin.</summary>
public class InvoiceLine
{
    public required string Sku { get; init; }

    public required string ProductName { get; init; }

    public required string VariantName { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal DiscountAmount { get; init; }

    public required decimal LineTotal { get; init; }

    /// <summary>
    /// The variant name is only worth printing when it says something. A
    /// single-variant product names its one variant after itself, and repeating
    /// it on the line is noise on a page that has to stay scannable.
    /// </summary>
    public bool ShowVariantName =>
        !string.IsNullOrWhiteSpace(VariantName)
        && !string.Equals(VariantName, ProductName, StringComparison.OrdinalIgnoreCase);
}
