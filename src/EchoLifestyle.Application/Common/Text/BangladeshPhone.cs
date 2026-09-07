using System.Text.RegularExpressions;

namespace EchoLifestyle.Application.Common.Text;

/// <summary>
/// Bangladeshi mobile numbers, normalised to one canonical form.
///
/// This matters more here than it looks. Orders arrive by Messenger, Instagram
/// and phone, and the number is the only thing that reliably identifies a
/// customer - names are written three different ways and half of them have no
/// email. If "01712345678", "+8801712345678" and "01712-345678" are stored as
/// three different customers, then order history, COD reliability and repeat
/// business all quietly stop working, and nothing looks broken.
///
/// Canonical form is the local eleven digits: 01XXXXXXXXX. That is how the
/// number is written in Bangladesh, and what every courier API expects.
/// </summary>
public static partial class BangladeshPhone
{
    /// <summary>
    /// A valid mobile number: 01, an operator digit, then eight more.
    /// 013-019 covers every current operator (Grameenphone, Robi, Banglalink,
    /// Teletalk, Airtel). Deliberately not a loose "eleven digits" check -
    /// letting a mistyped number through defeats the point of normalising.
    /// </summary>
    [GeneratedRegex(@"^01[3-9]\d{8}$")]
    private static partial Regex MobilePattern();

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex NonDigits();

    /// <summary>
    /// The canonical 01XXXXXXXXX form, or null when the input is not a valid
    /// Bangladeshi mobile number.
    ///
    /// Accepts every way people actually type one: +880 1712-345678,
    /// 8801712345678, 01712 345678, 1712345678.
    /// </summary>
    public static string? Normalise(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = NonDigits().Replace(input, string.Empty);

        // 00880… - the old international prefix, still typed by people who
        // learned to dial before mobiles.
        if (digits.StartsWith("00880", StringComparison.Ordinal))
        {
            digits = digits[5..];
        }
        else if (digits.StartsWith("880", StringComparison.Ordinal) && digits.Length == 13)
        {
            digits = digits[3..];
        }

        // A number pasted from a form that stripped the leading zero.
        if (digits.Length == 10 && digits[0] == '1')
        {
            digits = "0" + digits;
        }

        return MobilePattern().IsMatch(digits) ? digits : null;
    }

    public static bool IsValid(string? input) => Normalise(input) is not null;

    /// <summary>
    /// 01712-345678. Grouped the way it is read aloud, so a number is easy to
    /// check against a screenshot of a Messenger conversation.
    /// </summary>
    public static string Format(string? normalised)
    {
        if (string.IsNullOrWhiteSpace(normalised) || normalised.Length != 11)
        {
            return normalised ?? string.Empty;
        }

        return $"{normalised[..5]}-{normalised[5..]}";
    }

    /// <summary>
    /// 017*****678 - enough to recognise a number you already know, not enough
    /// to dial one you do not.
    ///
    /// What somebody without <c>Crm.Customer.ViewPii</c> sees. A salesperson
    /// can still confirm they are looking at the right customer; they cannot
    /// walk out with a contact list.
    /// </summary>
    public static string Mask(string? normalised)
    {
        if (string.IsNullOrWhiteSpace(normalised) || normalised.Length != 11)
        {
            return "•••••";
        }

        return $"{normalised[..3]}*****{normalised[8..]}";
    }
}
