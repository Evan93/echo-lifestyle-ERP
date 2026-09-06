using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EchoLifestyle.Application.Common.Text;

/// <summary>
/// URL segment generation.
///
/// Slugs end up in public addresses, so they are generated once and then left
/// alone: renaming "Cetaphil Gentle Cleanser" to fix a typo must not silently
/// move the page and break every link to it. The admin screens therefore offer
/// the slug as an editable field pre-filled from the name, rather than deriving
/// it on every save.
/// </summary>
public static partial class Slug
{
    public const int MaxLength = 160;

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex NonSlugCharacters();

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ValidSlug();

    /// <summary>
    /// Turns arbitrary text into a slug. Accented Latin characters fold to their
    /// base letters; anything else (Bangla, emoji, punctuation) is dropped.
    /// </summary>
    /// <remarks>
    /// A product named only in Bangla folds to an empty string. Callers must
    /// handle that rather than persisting an empty slug - see
    /// <see cref="IsValid"/>.
    /// </remarks>
    public static string From(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalised = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(normalised.Length);

        foreach (var ch in normalised)
        {
            // Combining marks are what is left of an accent after decomposition;
            // dropping them turns "é" into "e" rather than into nothing.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        var stripped = NonSlugCharacters().Replace(builder.ToString().Normalize(NormalizationForm.FormC), string.Empty);
        var collapsed = Separators().Replace(stripped, "-").Trim('-');

        return collapsed.Length <= MaxLength
            ? collapsed
            : collapsed[..MaxLength].TrimEnd('-');
    }

    /// <summary>
    /// True for a well-formed slug: lowercase alphanumeric groups separated by
    /// single hyphens, no leading or trailing hyphen.
    /// </summary>
    public static bool IsValid(string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= MaxLength && ValidSlug().IsMatch(slug);

    /// <summary>
    /// Appends -2, -3 ... until the slug is not taken. Used when a name collides
    /// with an existing one, so saving never fails on a slug the user did not
    /// choose.
    /// </summary>
    public static string MakeUnique(string slug, Func<string, bool> isTaken)
    {
        if (!isTaken(slug))
        {
            return slug;
        }

        // Bounded rather than while(true): a caller whose predicate never settles
        // should surface as a visible failure, not as a hung request.
        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{Truncate(slug, MaxLength - 5)}-{suffix}";

            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Could not find a free slug based on '{slug}'.");
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length].TrimEnd('-');
}
