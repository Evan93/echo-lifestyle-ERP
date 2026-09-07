using EchoLifestyle.Domain.Finance;

namespace EchoLifestyle.UnitTests.Finance;

/// <summary>
/// Which way each kind of money goes, and what each kind has to name.
///
/// These live in the domain because they are the reason the form has no
/// in-or-out box. A wrongly signed entry is the worst kind of mistake in a cash
/// log: the totals still add up, so nothing looks broken - the figure is simply
/// wrong by twice the amount, in a direction nobody thinks to check.
/// </summary>
public class CashTransactionRulesTests
{
    [Theory]
    [InlineData(CashKind.OrderCollection)]
    [InlineData(CashKind.PartnerCapital)]
    [InlineData(CashKind.Opening)]
    public void Money_that_only_ever_comes_in(CashKind kind) =>
        Assert.Equal(CashDirection.In, CashTransaction.NaturalDirection(kind));

    [Theory]
    [InlineData(CashKind.Expense)]
    [InlineData(CashKind.SupplierPayment)]
    [InlineData(CashKind.CustomerRefund)]
    [InlineData(CashKind.PartnerDrawing)]
    [InlineData(CashKind.CourierFee)]
    public void Money_that_only_ever_goes_out(CashKind kind) =>
        Assert.Equal(CashDirection.Out, CashTransaction.NaturalDirection(kind));

    /// <summary>
    /// A transfer leaves one pot and enters another; an adjustment corrects in
    /// whichever direction the mistake went. Null is the honest answer, and the
    /// service refuses to write either without a screen that asks.
    /// </summary>
    [Theory]
    [InlineData(CashKind.Transfer)]
    [InlineData(CashKind.Adjustment)]
    public void Money_that_genuinely_goes_both_ways(CashKind kind) =>
        Assert.Null(CashTransaction.NaturalDirection(kind));

    [Fact]
    public void Only_an_expense_needs_a_category()
    {
        Assert.True(CashTransaction.RequiresExpenseCategory(CashKind.Expense));

        foreach (var kind in Enum.GetValues<CashKind>().Where(k => k != CashKind.Expense))
        {
            Assert.False(CashTransaction.RequiresExpenseCategory(kind));
        }
    }

    [Fact]
    public void Only_capital_and_drawings_need_a_partner()
    {
        Assert.True(CashTransaction.RequiresPartner(CashKind.PartnerCapital));
        Assert.True(CashTransaction.RequiresPartner(CashKind.PartnerDrawing));
        Assert.False(CashTransaction.RequiresPartner(CashKind.Expense));

        // A courier fee is money out that concerns no partner at all. It reads
        // like a drawing only if you stop at "money leaving the business".
        Assert.False(CashTransaction.RequiresPartner(CashKind.CourierFee));
    }

    [Fact]
    public void Collections_and_refunds_are_the_kinds_that_name_an_order()
    {
        Assert.True(CashTransaction.RequiresSalesOrder(CashKind.OrderCollection));
        Assert.True(CashTransaction.RequiresSalesOrder(CashKind.CustomerRefund));

        // A courier fee arrives with a statement covering forty parcels. Tying
        // it to one of them would put the whole fee on that order's margin.
        Assert.False(CashTransaction.RequiresSalesOrder(CashKind.CourierFee));
    }

    [Fact]
    public void An_amount_is_positive_and_the_direction_carries_the_sign()
    {
        var outgoing = new CashTransaction
        {
            Direction = CashDirection.Out,
            Kind = CashKind.Expense,
            Amount = 1_250m,
        };

        Assert.Equal(-1_250m, outgoing.SignedAmount);
        Assert.False(outgoing.IsReversal);

        var reversal = new CashTransaction
        {
            Direction = CashDirection.In,
            Kind = CashKind.Expense,
            Amount = 1_250m,
            ReversesCashTransactionId = 42,
        };

        // The pair nets to nothing, which is the whole point of correcting by
        // reversal rather than by editing.
        Assert.Equal(0m, outgoing.SignedAmount + reversal.SignedAmount);
        Assert.True(reversal.IsReversal);
    }
}
