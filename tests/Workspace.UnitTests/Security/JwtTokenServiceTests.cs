using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Workspace.Domain.Entities;
using Workspace.Infrastructure.Options;
using Workspace.Infrastructure.Security;
using Xunit;

namespace Workspace.UnitTests.Security;

public class JwtTokenServiceTests
{
    private const string SigningKey = "unit-test-signing-key-at-least-32-bytes-long";

    private readonly JwtOptions options = new()
    {
        SigningKey = SigningKey,
        Issuer = "workspace-api-tests",
        Audience = "workspace-web-tests",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 14,
    };

    private readonly FixedClock clock = new();

    private JwtTokenService CreateService() => new(Options.Create(options), clock);

    private static User CreateUser() => new()
    {
        Email = "awadi@example.com",
        NormalizedEmail = "awadi@example.com",
        DisplayName = "Awadi",
        PasswordHash = "not-used-here",
    };

    [Fact]
    public async Task Access_token_validates_against_the_configured_parameters()
    {
        var user = CreateUser();
        var token = CreateService().CreateAccessToken(user);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, ValidationParameters());

        Assert.True(result.IsValid);
        Assert.Equal(user.Id.ToString(), result.Claims["sub"].ToString());
        Assert.Equal(user.Email, result.Claims["email"].ToString());
    }

    /// <summary>Tokens must carry identity only.</summary>
    [Fact]
    public async Task Access_token_carries_no_role_or_permission_claims()
    {
        var token = CreateService().CreateAccessToken(CreateUser());

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, ValidationParameters());

        Assert.True(result.IsValid);
        Assert.DoesNotContain("role", result.Claims.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("permissions", result.Claims.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("organizationId", result.Claims.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Access_token_expiry_follows_configuration()
    {
        var token = CreateService().CreateAccessToken(CreateUser());

        Assert.Equal(clock.UtcNow.AddMinutes(15), token.ExpiresAt);
        Assert.Equal(900, token.ExpiresInSeconds);
    }

    [Fact]
    public async Task Expired_access_token_is_rejected()
    {
        var token = CreateService().CreateAccessToken(CreateUser());

        clock.Advance(TimeSpan.FromMinutes(20));

        var parameters = ValidationParameters();
        parameters.LifetimeValidator = (_, expires, _, _) => expires > clock.UtcNow.UtcDateTime;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, parameters);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var token = CreateService().CreateAccessToken(CreateUser());

        var parameters = ValidationParameters();
        parameters.IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("a-completely-different-signing-key-value"));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, parameters);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Token_from_another_issuer_is_rejected()
    {
        var token = CreateService().CreateAccessToken(CreateUser());

        var parameters = ValidationParameters();
        parameters.ValidIssuer = "someone-else";

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, parameters);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Each_access_token_has_its_own_identifier()
    {
        var service = CreateService();
        var user = CreateUser();

        Assert.NotEqual(service.CreateAccessToken(user).Value, service.CreateAccessToken(user).Value);
    }

    [Fact]
    public void Opaque_tokens_are_unpredictable_and_url_safe()
    {
        var service = CreateService();

        var first = service.CreateOpaqueToken();
        var second = service.CreateOpaqueToken();

        Assert.NotEqual(first.Plaintext, second.Plaintext);
        Assert.NotEqual(first.Hash, second.Hash);

        // 256 bits, base64url-encoded, with no characters needing escaping.
        Assert.Equal(43, first.Plaintext.Length);
        Assert.All(first.Plaintext, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
    }

    [Fact]
    public void Hashing_is_deterministic_so_tokens_can_be_looked_up_by_hash()
    {
        var service = CreateService();
        var token = service.CreateOpaqueToken();

        Assert.Equal(token.Hash, service.HashToken(token.Plaintext));
        Assert.NotEqual(token.Hash, service.HashToken(token.Plaintext + "x"));
    }

    [Fact]
    public void Stored_hash_does_not_reveal_the_token()
    {
        var token = CreateService().CreateOpaqueToken();

        Assert.DoesNotContain(token.Plaintext, token.Hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_lifetime_follows_configuration() =>
        Assert.Equal(TimeSpan.FromDays(14), CreateService().RefreshTokenLifetime);

    private TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
        ValidateLifetime = false,
    };
}
