using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Workspace.Application.Abstractions;
using Workspace.Domain.Entities;
using ApplicationVerificationResult = Workspace.Application.Abstractions.PasswordVerificationResult;
using IdentityVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace Workspace.Infrastructure.Security;

/// <summary>Password hashing on top of ASP.NET Core Identity's hasher.</summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    /// <summary>OWASP's recommended minimum for PBKDF2-HMAC-SHA512 (2023 guidance).</summary>
    public const int IterationCount = 210_000;

    private readonly PasswordHasher<User> hasher = new(
        new OptionsWrapper<PasswordHasherOptions>(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = IterationCount,
        }));

    private static readonly User HashingSubject = new()
    {
        Email = "hashing@invalid",
        NormalizedEmail = "hashing@invalid",
        DisplayName = "hashing",
        PasswordHash = string.Empty,
    };

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        return hasher.HashPassword(HashingSubject, password);
    }

    public ApplicationVerificationResult Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return ApplicationVerificationResult.Failed;
        }

        return hasher.VerifyHashedPassword(HashingSubject, hash, password) switch
        {
            IdentityVerificationResult.Success => ApplicationVerificationResult.Success,
            IdentityVerificationResult.SuccessRehashNeeded => ApplicationVerificationResult.SuccessRehashNeeded,
            _ => ApplicationVerificationResult.Failed,
        };
    }
}
