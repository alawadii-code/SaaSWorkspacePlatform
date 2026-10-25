using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Workspace.Application.Common;

/// <summary>
/// Turns display names into URL-safe handles. Pure and unit-tested: slugs end
/// up in client routes, so surprises here are user-visible.
/// </summary>
public static class Slug
{
    public const int MaxLength = 48;

    /// <summary>
    /// Produces a lower-case, hyphen-separated slug. Accents are stripped,
    /// non-alphanumeric runs collapse to a single hyphen, and input that
    /// contains nothing usable (for example a name written entirely in a
    /// non-Latin script) falls back to a short random handle so creation never
    /// fails on a legitimate name.
    /// </summary>
    public static string From(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var decomposed = input.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasHyphen = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && builder.Length > 0)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].Trim('-');
        }

        return slug.Length == 0 ? RandomHandle() : slug;
    }

    /// <summary>
    /// Appends a short random suffix, used when the preferred slug is already
    /// taken within its scope.
    /// </summary>
    public static string WithSuffix(string slug, string suffix)
    {
        var trimmed = slug.Length + suffix.Length + 1 > MaxLength
            ? slug[..(MaxLength - suffix.Length - 1)].Trim('-')
            : slug;

        return $"{trimmed}-{suffix}";
    }

    private static string RandomHandle() =>
        $"ws-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";
}
