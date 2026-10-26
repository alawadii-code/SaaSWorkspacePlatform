using System.Net;
using System.Net.Http.Json;
using Workspace.Application.Auth;
using Workspace.IntegrationTests.Fixtures;
using Xunit;

namespace Workspace.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task Register_signs_the_user_in_and_me_returns_their_profile()
    {
        var user = await fixture.CreateUserAsync("register");

        var response = await user.Client.GetAsync("/api/auth/me");
        await response.EnsureSuccessAsync();

        var me = await response.Content.ReadFromJsonAsync<MeResponse>(ApiScenarios.Json);

        Assert.Equal(user.Email, me!.User.Email);
        Assert.Equal(ApiScenarios.DisplayName, me.User.DisplayName);

        // Registration creates an account, not an organization.
        Assert.Empty(me.Organizations);
    }

    [Fact]
    public async Task Protected_endpoints_reject_anonymous_callers()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoints_reject_a_forged_token()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not.a.real.token");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registering_the_same_email_twice_conflicts()
    {
        var user = await fixture.CreateUserAsync("duplicate");
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = user.Email.ToUpperInvariant(), password = ApiScenarios.Password, displayName = "Someone Else" },
            ApiScenarios.Json);

        // Case-insensitive: the normalized column carries the unique index.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Weak_passwords_are_rejected_with_field_errors()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"weak-{Guid.NewGuid():N}@example.test", password = "short", displayName = "Sam Rivers" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("validation_failed", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Login_succeeds_with_the_right_password()
    {
        var user = await fixture.CreateUserAsync("login");
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = ApiScenarios.Password },
            ApiScenarios.Json);

        await response.EnsureSuccessAsync();

        var auth = await response.Content.ReadFromJsonAsync<AuthResult>(ApiScenarios.Json);

        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
        Assert.Equal("Bearer", auth.TokenType);
        Assert.Equal(user.UserId, auth.User.Id);
    }

    /// <summary>A wrong password and an unknown address must be indistinguishable, or the endpoint becomes an account-enumeration oracle.</summary>
    [Fact]
    public async Task Wrong_password_and_unknown_account_fail_identically()
    {
        var user = await fixture.CreateUserAsync("enumeration");
        var client = fixture.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = "definitely-not-the-password" },
            ApiScenarios.Json);

        var unknownAccount = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = $"nobody-{Guid.NewGuid():N}@example.test", password = ApiScenarios.Password },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);

        var first = await wrongPassword.Content.ReadAsStringAsync();
        var second = await unknownAccount.Content.ReadAsStringAsync();

        Assert.Equal(StripCorrelation(first), StripCorrelation(second));
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_replaying_the_old_one_kills_the_session()
    {
        var user = await fixture.CreateUserAsync("rotation");
        var client = fixture.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = ApiScenarios.Password },
            ApiScenarios.Json);

        await login.EnsureSuccessAsync();

        var first = await login.Content.ReadFromJsonAsync<AuthResult>(ApiScenarios.Json);

        var refreshed = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = first!.RefreshToken },
            ApiScenarios.Json);

        await refreshed.EnsureSuccessAsync();

        var second = await refreshed.Content.ReadFromJsonAsync<AuthResult>(ApiScenarios.Json);

        Assert.NotEqual(first.RefreshToken, second!.RefreshToken);

        var replay = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = first.RefreshToken },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var afterReplay = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = second.RefreshToken },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
    }

    [Fact]
    public async Task Refresh_works_from_the_cookie_alone()
    {
        var user = await fixture.CreateUserAsync("cookie");
        var client = fixture.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = ApiScenarios.Password },
            ApiScenarios.Json);

        await login.EnsureSuccessAsync();

        // No body at all: the HttpOnly cookie set by login is the credential.
        var refreshed = await client.PostAsync("/api/auth/refresh", content: null);

        await refreshed.EnsureSuccessAsync();

        var auth = await refreshed.Content.ReadFromJsonAsync<AuthResult>(ApiScenarios.Json);

        Assert.False(string.IsNullOrWhiteSpace(auth!.AccessToken));
    }

    [Fact]
    public async Task Refresh_rejects_a_token_that_was_never_issued()
    {
        var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session()
    {
        var user = await fixture.CreateUserAsync("logout");
        var client = fixture.CreateClient();

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = ApiScenarios.Password },
            ApiScenarios.Json);

        await login.EnsureSuccessAsync();

        var auth = await login.Content.ReadFromJsonAsync<AuthResult>(ApiScenarios.Json);

        var logout = await client.PostAsJsonAsync(
            "/api/auth/logout",
            new { refreshToken = auth!.RefreshToken },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = auth.RefreshToken },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    /// <summary>Online guessing is blunted by a lockout, and the lockout is not disclosed: the response stays the same generic 401 even once the right password is supplied.</summary>
    [Fact]
    public async Task Repeated_failures_lock_the_account_without_saying_so()
    {
        var user = await fixture.CreateUserAsync("lockout");
        var client = fixture.CreateClient();

        for (var attempt = 0; attempt < AuthService.MaxFailedAttempts; attempt++)
        {
            var failed = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = user.Email, password = $"wrong-password-{attempt}" },
                ApiScenarios.Json);

            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var withCorrectPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = user.Email, password = ApiScenarios.Password },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, withCorrectPassword.StatusCode);

        var body = await withCorrectPassword.Content.ReadAsStringAsync();

        Assert.DoesNotContain("lock", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_carry_a_correlation_id()
    {
        var user = await fixture.CreateUserAsync("correlation");

        var response = await user.Client.GetAsync("/api/auth/me");

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values!.First()));
    }

    /// <summary>Removes the per-request correlation id so two error bodies can be compared for everything that actually describes the failure.</summary>
    private static string StripCorrelation(string body)
    {
        var index = body.IndexOf("\"correlationId\"", StringComparison.Ordinal);

        return index < 0 ? body : body[..index];
    }
}
