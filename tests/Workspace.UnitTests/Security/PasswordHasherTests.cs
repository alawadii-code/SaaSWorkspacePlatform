using Workspace.Application.Abstractions;
using Workspace.Infrastructure.Security;
using Xunit;

namespace Workspace.UnitTests.Security;

public class PasswordHasherTests
{
    private readonly IdentityPasswordHasher hasher = new();

    [Fact]
    public void Verifies_a_correct_password()
    {
        var hash = hasher.Hash("correct horse battery staple");

        Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(hash, "correct horse battery staple"));
    }

    [Fact]
    public void Rejects_an_incorrect_password()
    {
        var hash = hasher.Hash("correct horse battery staple");

        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify(hash, "incorrect horse battery staple"));
    }

    [Fact]
    public void Is_case_sensitive()
    {
        var hash = hasher.Hash("Correct Horse Battery");

        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify(hash, "correct horse battery"));
    }

    /// <summary>
    /// A per-password random salt means identical passwords hash differently,
    /// so a stolen table cannot be attacked once for every account at a time.
    /// </summary>
    [Fact]
    public void Salts_every_hash()
    {
        var first = hasher.Hash("same password here");
        var second = hasher.Hash("same password here");

        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(first, "same password here"));
        Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(second, "same password here"));
    }

    [Fact]
    public void Hash_does_not_contain_the_password() =>
        Assert.DoesNotContain("battery", hasher.Hash("correct horse battery staple"), StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-hash")]
    [InlineData("AQAAAAIAAYagAAAAE")]
    public void Malformed_or_empty_hashes_fail_without_throwing(string hash) =>
        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify(hash, "any password"));

    [Fact]
    public void Empty_candidate_password_fails() =>
        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify(hasher.Hash("a real password"), string.Empty));

    [Fact]
    public void Hashing_an_empty_password_is_a_programming_error() =>
        Assert.Throws<ArgumentException>(() => hasher.Hash(string.Empty));

    [Fact]
    public void Handles_unicode_passwords()
    {
        const string password = "مرحبا بالعالم ١٢٣";

        Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(hasher.Hash(password), password));
    }
}
