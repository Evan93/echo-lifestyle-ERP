using EchoLifestyle.Application.Common.Text;

namespace EchoLifestyle.UnitTests.Text;

public class SlugTests
{
    [Theory]
    [InlineData("The Ordinary", "the-ordinary")]
    [InlineData("  Cetaphil  Gentle   Cleanser ", "cetaphil-gentle-cleanser")]
    [InlineData("L'Oréal Paris", "loreal-paris")]
    [InlineData("Vitamin C 20% + HA", "vitamin-c-20-ha")]
    [InlineData("Night Creams", "night-creams")]
    [InlineData("already-a-slug", "already-a-slug")]
    public void Generates_a_clean_slug(string input, string expected)
    {
        Assert.Equal(expected, Slug.From(input));
    }

    [Fact]
    public void Accented_letters_fold_to_their_base_letter_rather_than_vanishing()
    {
        // "loreal" not "lral" - dropping the letter entirely would make two
        // different brands collide.
        Assert.Equal("creme-brulee", Slug.From("Crème Brûlée"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_input_gives_an_empty_slug(string? input)
    {
        Assert.Equal(string.Empty, Slug.From(input));
    }

    [Fact]
    public void Text_with_no_latin_characters_gives_an_empty_slug()
    {
        // Bangla folds to nothing. Callers must ask the user for an address
        // rather than saving a blank one - the services check for exactly this.
        Assert.Equal(string.Empty, Slug.From("রূপচর্চা"));
    }

    [Fact]
    public void Punctuation_disappears_but_a_space_becomes_a_separator()
    {
        // Easy to assume away, and wrong in a way that only shows up as a
        // surprising address: the apostrophe leaves nothing behind, the space
        // leaves a hyphen. Two spellings of the same brand therefore do NOT
        // collide.
        Assert.Equal("loreal", Slug.From("L'Oreal"));
        Assert.Equal("l-oreal", Slug.From("L Oreal"));
    }

    [Fact]
    public void Trailing_non_latin_text_leaves_no_trailing_hyphen()
    {
        // A Bangla subtitle folds away entirely; what is left must not end in
        // the separator that used to sit before it.
        Assert.Equal("cerave", Slug.From("Cerave রূপচর্চা"));
    }

    [Fact]
    public void Leading_and_trailing_separators_are_trimmed()
    {
        Assert.Equal("skincare", Slug.From("--- Skincare!!! ---"));
    }

    [Fact]
    public void Long_names_are_truncated_without_a_trailing_hyphen()
    {
        var generated = Slug.From(string.Join(" ", Enumerable.Repeat("moisturiser", 40)));

        Assert.True(generated.Length <= Slug.MaxLength);
        Assert.False(generated.EndsWith('-'));
    }

    [Theory]
    [InlineData("the-ordinary", true)]
    [InlineData("a1", true)]
    [InlineData("The-Ordinary", false)]      // uppercase
    [InlineData("the--ordinary", false)]     // doubled separator
    [InlineData("-ordinary", false)]         // leading hyphen
    [InlineData("ordinary-", false)]         // trailing hyphen
    [InlineData("the ordinary", false)]      // space
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_the_shape_of_a_typed_slug(string? slug, bool expected)
    {
        Assert.Equal(expected, Slug.IsValid(slug));
    }

    [Fact]
    public void An_unused_slug_is_returned_unchanged()
    {
        Assert.Equal("cerave", Slug.MakeUnique("cerave", _ => false));
    }

    [Fact]
    public void A_taken_slug_gains_the_lowest_free_suffix()
    {
        var taken = new HashSet<string> { "cerave", "cerave-2", "cerave-3" };

        Assert.Equal("cerave-4", Slug.MakeUnique("cerave", taken.Contains));
    }

    [Fact]
    public void A_suffixed_slug_still_respects_the_length_limit()
    {
        var longSlug = new string('a', Slug.MaxLength);

        var unique = Slug.MakeUnique(longSlug, s => s == longSlug);

        Assert.True(unique.Length <= Slug.MaxLength);
        Assert.NotEqual(longSlug, unique);
    }

    [Fact]
    public void A_predicate_that_never_settles_fails_loudly_rather_than_hanging()
    {
        // A silent infinite loop here would present as a hung request with no
        // error anywhere. Bounded retries turn it into something findable.
        Assert.Throws<InvalidOperationException>(() => Slug.MakeUnique("stuck", _ => true));
    }
}
