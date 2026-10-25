using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Workspace.Application.Abstractions;
using Workspace.Domain.Entities;
using Workspace.Infrastructure.Options;

namespace Workspace.Infrastructure.Security;

/// <summary>
/// Issues signed access tokens and the opaque tokens used for refresh and
/// invitation links.
/// </summary>
/// <remarks>
/// <para>
/// Access tokens are HMAC-SHA256 JWTs carrying identity claims only — subject,
/// email, display name, and a token id. No role or permission claim is issued,
/// because a token is valid for minutes while a role change must take effect
/// at once: the server resolves the caller's membership on every request
/// instead.
/// </para>
/// <para>
/// Refresh and invitation tokens are not JWTs. They are 256 bits from the
/// system CSPRNG, URL-encoded, and stored only as SHA-256 hashes — a hash of a
/// high-entropy random value needs no salt or stretching, since there is
/// nothing to guess.
/// </para>
/// </remarks>
public sealed class JwtTokenService : ITokenService
{
    private const int TokenBytes = 32;

    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions options;
    private readonly IClock clock;
    private readonly SigningCredentials credentials;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        this.options = options.Value;
        this.clock = clock;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this.options.SigningKey));
        credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public TimeSpan RefreshTokenLifetime => options.RefreshTokenLifetime;

    public AccessToken CreateAccessToken(User user)
    {
        var now = clock.UtcNow;
        var expiresAt = now.Add(options.AccessTokenLifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = user.Id.ToString(),
                ["email"] = user.Email,
                ["name"] = user.DisplayName,
                ["jti"] = Guid.CreateVersion7().ToString(),
            },
        };

        return new AccessToken(
            Handler.CreateToken(descriptor),
            expiresAt,
            (int)options.AccessTokenLifetime.TotalSeconds);
    }

    public (string Plaintext, string Hash) CreateRefreshToken() => CreateOpaqueToken();

    public (string Plaintext, string Hash) CreateOpaqueToken()
    {
        var plaintext = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

        return (plaintext, HashToken(plaintext));
    }

    public string HashToken(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
    }
}
