using Workspace.Application.Auth;
using Xunit;

namespace Workspace.UnitTests.Auth;

public class PasswordPolicyTests
{
    [Fact]
    public void Accepts_a_long_passphrase() =>
        Assert.Null(PasswordPolicy.Validate("correct horse battery staple"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_missing_password(string? password) =>
        Assert.NotNull(PasswordPolicy.Validate(password));

    [Fact]
    public void Rejects_passwords_shorter_than_the_minimum() =>
        Assert.Contains("at least", PasswordPolicy.Validate(new string('a', PasswordPolicy.MinLength - 1))!);

    [Fact]
    public void Rejects_passwords_longer_than_the_maximum() =>
        Assert.Contains("at most", PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength + 1))!);

    /// <summary>The length cap is a denial-of-service guard: hashing is on purpose ofc expensive, so unbounded input wud let one request burn CPU.</summary>
    [Fact]
    public void Accepts_a_password_at_exactly_the_maximum_length() =>
        Assert.Null(PasswordPolicy.Validate("vA" + new string('x', PasswordPolicy.MaxLength - 2)));

    [Theory]
    [InlineData("password1234")]
    [InlineData("Password1234")]
    [InlineData("qwerty123456")]
    [InlineData("iloveyou1234")]
    public void Rejects_common_guesses(string password) =>
        Assert.Contains("too common", PasswordPolicy.Validate(password)!);

    [Fact]
    public void Rejects_a_single_repeated_character() =>
        Assert.Contains("repeated character", PasswordPolicy.Validate(new string('q', 20))!);

    [Fact]
    public void Rejects_a_password_containing_the_email_local_part() =>
        Assert.Contains("email", PasswordPolicy.Validate("awadi-workspace-login", "awadi@example.com")!);

    [Fact]
    public void Rejects_a_password_containing_the_display_name() =>
        Assert.Contains("name", PasswordPolicy.Validate("mohamed-is-my-password", null, "Mohamed Al-Awadi")!);

    /// <summary>Short fragments must not false-positive: a two or three letter name wud otherwise reject almost evry password that contains it.</summary>
    [Fact]
    public void Short_name_fragments_do_not_reject_valid_passwords() =>
        Assert.Null(PasswordPolicy.Validate("thunderous valley crossing", "al@example.com", "Al B"));

    [Fact]
    public void Context_matching_ignores_case() =>
        Assert.NotNull(PasswordPolicy.Validate("AWADI-secure-passphrase", "awadi@example.com"));
}
