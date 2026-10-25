using System.Collections.Frozen;

namespace Workspace.Application.Auth;

/// <summary>
/// Password rules, following NIST SP 800-63B: length is what matters, so there
/// are no composition requirements (no "must contain a symbol"), but obvious
/// and context-specific guesses are blocked.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;

    /// <summary>
    /// Upper bound so a single request cannot make the server hash megabytes
    /// of input as a denial-of-service vector.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// A deliberately small deny list of the guesses an online attacker tries
    /// first. A production deployment would check a breached-password corpus
    /// (for example a k-anonymity range query against Pwned Passwords); that
    /// is an outbound dependency this trial does not take on.
    /// </summary>
    private static readonly FrozenSet<string> CommonPasswords = new[]
    {
        "password", "password1", "password123", "passw0rd", "123456", "1234567",
        "12345678", "123456789", "1234567890", "qwerty", "qwerty123", "letmein",
        "welcome", "welcome1", "admin", "administrator", "iloveyou", "monkey",
        "dragon", "football", "baseball", "sunshine", "princess", "changeme",
        "secret", "trustno1", "abc123", "abcd1234", "p@ssword", "p@ssw0rd",
        "workspace", "saas", "test1234", "demo1234",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates a candidate password against <paramref name="email"/> and
    /// <paramref name="displayName"/> context. Returns <c>null</c> when
    /// acceptable, otherwise the reason to show the user.
    /// </summary>
    public static string? Validate(string? password, string? email = null, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "Password is required.";
        }

        if (password.Length < MinLength)
        {
            return $"Password must be at least {MinLength} characters.";
        }

        if (password.Length > MaxLength)
        {
            return $"Password must be at most {MaxLength} characters.";
        }

        var normalized = Simplify(password);

        if (CommonPasswords.Contains(normalized))
        {
            return "Password is too common. Choose something harder to guess.";
        }

        if (HasSingleRepeatedCharacter(password))
        {
            return "Password cannot be a single repeated character.";
        }

        if (ContainsContext(normalized, email))
        {
            return "Password cannot contain your email address.";
        }

        if (ContainsContext(normalized, displayName))
        {
            return "Password cannot contain your name.";
        }

        return null;
    }

    /// <summary>Strips trailing digits so "password2024" matches "password".</summary>
    private static string Simplify(string password) => password.Trim().TrimEnd('!', '?', '.', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0');

    private static bool HasSingleRepeatedCharacter(string password)
    {
        var first = password[0];
        return password.All(c => c == first);
    }

    private static bool ContainsContext(string password, string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return false;
        }

        // Compare against the local part of an email, and against each word of
        // a display name, ignoring very short fragments that would false-positive.
        var fragments = context.Split(['@', ' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return fragments
            .Where(fragment => fragment.Length >= 4)
            .Any(fragment => password.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
