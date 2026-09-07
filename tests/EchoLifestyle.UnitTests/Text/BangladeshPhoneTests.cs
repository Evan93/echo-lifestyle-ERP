using EchoLifestyle.Application.Common.Text;

namespace EchoLifestyle.UnitTests.Text;

/// <summary>
/// The phone number is the customer's identity, so these tests are really about
/// one question: do two people who typed the same number the two most natural
/// ways end up as one customer, or two?
/// </summary>
public class BangladeshPhoneTests
{
    [Theory]
    [InlineData("01712345678")]
    [InlineData("+8801712345678")]
    [InlineData("8801712345678")]
    [InlineData("008801712345678")]
    [InlineData("01712-345678")]
    [InlineData("017 1234 5678")]
    [InlineData("+880 1712 345678")]
    [InlineData("1712345678")]
    [InlineData(" 01712345678 ")]
    public void Every_way_somebody_writes_one_number_normalises_to_the_same_thing(string typed)
    {
        Assert.Equal("01712345678", BangladeshPhone.Normalise(typed));
    }

    [Theory]
    [InlineData("013")]
    [InlineData("01312345678")]
    [InlineData("01412345678")]
    [InlineData("01512345678")]
    [InlineData("01612345678")]
    [InlineData("01812345678")]
    [InlineData("01912345678")]
    public void Every_current_operator_prefix_is_accepted(string typed)
    {
        // 013 alone is too short and must fail; the rest are real numbers.
        var expected = typed.Length == 11;

        Assert.Equal(expected, BangladeshPhone.IsValid(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0171234567")]      // ten digits - one short
    [InlineData("017123456789")]    // twelve - one long
    [InlineData("01212345678")]     // 012 is not an operator
    [InlineData("01012345678")]     // nor is 010
    [InlineData("02955123456")]     // a Dhaka landline
    [InlineData("447700900123")]    // a UK number
    [InlineData("not a number")]
    public void Anything_that_is_not_a_bangladeshi_mobile_is_refused(string? typed)
    {
        Assert.Null(BangladeshPhone.Normalise(typed));
        Assert.False(BangladeshPhone.IsValid(typed));
    }

    [Fact]
    public void A_number_with_the_country_code_but_the_wrong_length_is_not_salvaged()
    {
        // 880 is only stripped from a full thirteen digits. Guessing at
        // anything shorter would invent a number nobody typed.
        Assert.Null(BangladeshPhone.Normalise("880171234567"));
    }

    [Fact]
    public void Formatting_groups_it_the_way_it_is_read_aloud()
    {
        Assert.Equal("01712-345678", BangladeshPhone.Format("01712345678"));
    }

    [Fact]
    public void Formatting_leaves_anything_unexpected_alone()
    {
        // Alternate numbers are allowed to be landlines and oddities. Format
        // must not mangle one into something that looks like a mobile.
        Assert.Equal("02-9551234", BangladeshPhone.Format("02-9551234"));
        Assert.Equal(string.Empty, BangladeshPhone.Format(null));
    }

    [Fact]
    public void Masking_shows_enough_to_recognise_and_not_enough_to_dial()
    {
        var masked = BangladeshPhone.Mask("01712345678");

        Assert.Equal("017*****678", masked);
        Assert.Equal(11, masked.Length);

        // The middle five digits - the part that identifies the subscriber -
        // are gone.
        Assert.DoesNotContain("12345", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Masking_something_that_is_not_a_number_reveals_nothing()
    {
        Assert.Equal("•••••", BangladeshPhone.Mask("02-9551234"));
        Assert.Equal("•••••", BangladeshPhone.Mask(null));
    }
}
