using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Workspace.Application.Abstractions;
using Workspace.Domain.Entities;
using ApplicationVerificationResult = Workspace.Application.Abstractions.PasswordVerificationResult;
using IdentityVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace Workspace.Infrastructure.Security;

/// <summary>
/// Password hashing on top of ASP.NET Core Identity's hasher.
/// </summary>
/// <remarks>
/// <para>
/// Format "v3": PBKDF2-HMAC-SHA512, a 128-bit random salt per password, and an
/// iteration count set below rather than left at the framework default.
/// Verification is constant-time and the embedded parameters let stored hashes
/// be upgraded later without invalidating anyone's password.
/// </para>
/// <para>
/// Why not Argon2id, which is the stronger primitive: every .NET
/// implementation of it is a third-party native dependency, while this one
/// ships with the framework, is widely reviewed, and is an accepted OWASP
/// option at this iteration count. Swapping it later is a change to this one
/// class — <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>
/// already drives transparent re-hashing on next sign-in.
/// </para>
/// </remarks>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// OWASP's recommended minimum for PBKDF2-HMAC-SHA512 (2023 guidance).
    /// </summary>
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
