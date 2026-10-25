using Workspace.Application.Common;
using Xunit;

namespace Workspace.UnitTests.Common;

public class SlugTests
{
    [Theory]
    [InlineData("Acme Corp", "acme-corp")]
    [InlineData("  Trimmed  ", "trimmed")]
    [InlineData("Website Redesign 2027", "website-redesign-2027")]
    [InlineData("Multiple   spaces", "multiple-spaces")]
    [InlineData("Symbols!@#$%^&*()here", "symbols-here")]
    [InlineData("--leading-and-trailing--", "leading-and-trailing")]
    [InlineData("MiXeD CaSe", "mixed-case")]
    public void Produces_url_safe_handles(string input, string expected) =>
        Assert.Equal(expected, Slug.From(input));

    [Fact]
    public void Strips_accents_rather_than_dropping_the_word() =>
        Assert.Equal("equipe-francaise", Slug.From("Équipe Française"));

    [Fact]
    public void Truncates_to_the_maximum_length()
    {
        var slug = Slug.From(new string('a', Slug.MaxLength * 2));

        Assert.Equal(Slug.MaxLength, slug.Length);
    }

    /// <summary>
    /// An organization named entirely in a non-Latin script must still be
    /// creatable: the slug falls back to a random handle instead of failing.
    /// </summary>
    [Theory]
    [InlineData("شركة")]
    [InlineData("日本語")]
    [InlineData("!!!")]
    [InlineData("")]
    public void Falls_back_to_a_random_handle_when_nothing_is_usable(string input)
    {
        var slug = Slug.From(input);

        Assert.StartsWith("ws-", slug, StringComparison.Ordinal);
        Assert.True(slug.Length > 3);
    }

    [Fact]
    public void Random_fallback_handles_are_distinct() =>
        Assert.NotEqual(Slug.From("!!!"), Slug.From("!!!"));

    [Fact]
    public void WithSuffix_appends_and_stays_within_the_limit()
    {
        var suffixed = Slug.WithSuffix(new string('a', Slug.MaxLength), "abc123");

        Assert.EndsWith("-abc123", suffixed, StringComparison.Ordinal);
        Assert.True(suffixed.Length <= Slug.MaxLength);
    }

    [Fact]
    public void WithSuffix_keeps_short_slugs_intact() =>
        Assert.Equal("acme-corp-7f3a19", Slug.WithSuffix("acme-corp", "7f3a19"));

    [Fact]
    public void Throws_on_null_input() =>
        Assert.Throws<ArgumentNullException>(() => Slug.From(null!));
}
