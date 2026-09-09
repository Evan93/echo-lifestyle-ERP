using EchoLifestyle.Application.Administration.CompanyProfile;
using EchoLifestyle.Application.Sales.Orders;
using EchoLifestyle.Domain.Sales;
using EchoLifestyle.Web.Areas.BackOffice.Models.Sales;

namespace EchoLifestyle.IntegrationTests;

/// <summary>
/// The document that leaves the building.
///
/// No database here on purpose - this is about what the printed sheet is
/// allowed to say, which is a property of the mapping and nothing else.
/// </summary>
public class InvoiceViewModelTests
{
    private static SalesOrderDetail Order(
        SalesOrderStatus status = SalesOrderStatus.Confirmed,
        PaymentMethod payment = PaymentMethod.CashOnDelivery,
        decimal collected = 0m) => new()
        {
            Id = 42,
            Number = "SO-2609-0007",
            OrderDate = new DateOnly(2026, 9, 9),
            Status = status,
            PaymentMethod = payment,

            RecipientName = "Rumana Akter",
            RecipientPhone = "01711223344",
            AddressLine = "House 12, Road 5",
            AreaOrThana = "Dhanmondi",
            DistrictName = "Dhaka",

            SubTotal = 1400m,
            DiscountAmount = 100m,
            DeliveryCharge = 60m,
            GrandTotal = 1360m,
            AmountCollected = collected,

            // Present on the order, and the whole point of the test below is
            // that they cannot reach the sheet.
            CostOfGoods = 820m,

            Lines =
            [
                new SalesOrderLineDetail
                {
                    Id = 1,
                    Sku = "SUN-PB-180",
                    ProductName = "Sunsilk Power Bond",
                    VariantName = "Sunsilk Power Bond",
                    Quantity = 2m,
                    UnitPrice = 400m,
                    LineTotal = 800m,
                    CostOfGoods = 460m,
                },
                new SalesOrderLineDetail
                {
                    Id = 2,
                    Sku = "LAK-LIP-R04",
                    ProductName = "Lakme Lipstick",
                    VariantName = "Ruby Red",
                    Quantity = 1m,
                    UnitPrice = 600m,
                    DiscountAmount = 100m,
                    LineTotal = 500m,
                    CostOfGoods = 360m,
                },
            ],
        };

    private static CompanyDetail Company() => new()
    {
        Id = 1,
        Name = "Echo Lifestyle",
        AddressLine1 = "Flat 4B, House 21",
        City = "Dhaka",
        PostalCode = "1207",
        Phone = "01700000000",
    };

    // -----------------------------------------------------------------------

    /// <summary>
    /// The rule the whole model exists for. A sheet that goes to a customer
    /// must not be able to tell them what Echo paid for the goods - and the
    /// guard is that the fields are absent, not merely unrendered, because a
    /// hidden field is still in the page source.
    /// </summary>
    [Fact]
    public void Nothing_on_the_invoice_can_carry_cost_or_margin()
    {
        foreach (var type in new[] { typeof(InvoiceViewModel), typeof(InvoiceLine) })
        {
            var leaked = type.GetProperties()
                .Where(p => p.Name.Contains("Cost", StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains("Margin", StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{type.Name}.{p.Name}")
                .ToList();

            Assert.True(
                leaked.Count == 0,
                $"The printed invoice must not carry buying prices: {string.Join(", ", leaked)}");
        }
    }

    [Fact]
    public void A_cash_on_delivery_order_asks_the_courier_for_the_whole_total()
    {
        var invoice = InvoiceViewModel.From(Order(), Company(), autoPrint: false);

        Assert.False(invoice.IsSettled);
        Assert.Equal("Cash to collect", invoice.CollectLabel);
        Assert.Equal(1360m, invoice.AmountToCollect);
    }

    /// <summary>
    /// The bug this test exists for: the first version printed the amount
    /// already paid in the box a courier reads for the amount to collect, so a
    /// settled order showed a large 960 where it should have shown nothing to
    /// collect. Asking for money that has already changed hands is the one
    /// failure the box was put on the page to prevent.
    /// </summary>
    [Fact]
    public void A_settled_order_asks_for_nothing_rather_than_showing_what_was_paid()
    {
        var invoice = InvoiceViewModel.From(
            Order(payment: PaymentMethod.PaidInAdvance, collected: 1360m),
            Company(),
            autoPrint: false);

        Assert.True(invoice.IsSettled);
        Assert.Equal(0m, invoice.AmountToCollect);
        Assert.Equal("Nothing to collect", invoice.CollectLabel);

        // The figure printed large is the amount due, and it is not the total.
        Assert.NotEqual(invoice.GrandTotal, invoice.AmountToCollect);
    }

    /// <summary>
    /// Part paid: the balance is what is asked for, and it is named a balance
    /// so nobody reads it as the price of the goods.
    /// </summary>
    [Fact]
    public void A_part_paid_order_asks_only_for_the_balance()
    {
        var invoice = InvoiceViewModel.From(
            Order(payment: PaymentMethod.Bkash, collected: 1000m),
            Company(),
            autoPrint: false);

        Assert.False(invoice.IsSettled);
        Assert.Equal(360m, invoice.AmountToCollect);
        Assert.Equal("Balance to collect", invoice.CollectLabel);
    }

    /// <summary>
    /// An over-collection - a rounded-up cash payment, say - is still nothing
    /// to collect rather than a negative figure printed on a customer's sheet.
    /// </summary>
    [Fact]
    public void Collecting_more_than_the_total_still_reads_as_settled()
    {
        var invoice = InvoiceViewModel.From(
            Order(collected: 1400m), Company(), autoPrint: false);

        Assert.True(invoice.IsSettled);
        Assert.Equal("Nothing to collect", invoice.CollectLabel);
    }

    [Fact]
    public void The_courier_reference_reads_as_one_line_or_not_at_all()
    {
        Assert.Null(InvoiceViewModel.From(Order(), Company(), false).CourierReference);

        var order = Order();
        order.CourierName = "Steadfast";
        order.ConsignmentNumber = "123";

        Assert.Equal(
            "Steadfast · 123",
            InvoiceViewModel.From(order, Company(), false).CourierReference);
    }

    [Fact]
    public void A_draft_says_so_on_the_sheet()
    {
        Assert.True(InvoiceViewModel
            .From(Order(SalesOrderStatus.Draft), Company(), false).IsDraft);

        Assert.False(InvoiceViewModel
            .From(Order(SalesOrderStatus.Packed), Company(), false).IsDraft);
    }

    /// <summary>
    /// A single-variant product names its variant after itself, and printing
    /// "Sunsilk Power Bond - Sunsilk Power Bond" on a packing sheet is noise on
    /// a page somebody has to read quickly.
    /// </summary>
    [Fact]
    public void A_variant_name_is_printed_only_when_it_says_something()
    {
        var invoice = InvoiceViewModel.From(Order(), Company(), autoPrint: false);

        Assert.False(invoice.Lines[0].ShowVariantName);
        Assert.True(invoice.Lines[1].ShowVariantName);
    }

    /// <summary>
    /// Settings that have not been filled in yet leave no gap. A blank line
    /// where the city should be reads as a broken document rather than an
    /// incomplete setting.
    /// </summary>
    [Fact]
    public void An_unfinished_company_address_prints_without_holes_in_it()
    {
        var invoice = InvoiceViewModel.From(
            Order(),
            new CompanyDetail { Id = 1, Name = "Echo Lifestyle" },
            autoPrint: false);

        Assert.Empty(invoice.CompanyAddressLines);
        Assert.Equal("Echo Lifestyle", invoice.CompanyName);
    }

    /// <summary>
    /// The address the courier reads, in the order a Bangladeshi address is
    /// read in: street, then area, then district.
    /// </summary>
    [Fact]
    public void The_delivery_address_reads_as_a_courier_reads_it()
    {
        var invoice = InvoiceViewModel.From(Order(), Company(), autoPrint: false);

        Assert.Equal("House 12, Road 5, Dhanmondi, Dhaka", invoice.DeliveryAddress);
    }
}
