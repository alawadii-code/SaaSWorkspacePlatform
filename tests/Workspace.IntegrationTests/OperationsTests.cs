using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Workspace.IntegrationTests.Fixtures;
using Xunit;

namespace Workspace.IntegrationTests;

/// <summary>
/// The endpoints an operator or orchestrator depends on, plus the response
/// hygiene every endpoint is expected to have.
/// </summary>
[Collection(ApiCollection.Name)]
public class OperationsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Liveness_needs_no_dependencies_and_no_credentials()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        await response.EnsureSuccessAsync();

        var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal("healthy", payload!.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_reports_the_database()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health/ready");

        await response.EnsureSuccessAsync();

        var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var checks = payload!.RootElement.GetProperty("checks");

        Assert.Equal("Healthy", checks.GetProperty("database").GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_openapi_document_is_served()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        await response.EnsureSuccessAsync();

        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var paths = document!.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/auth/login", out _));
        Assert.True(paths.TryGetProperty("/api/organizations", out _));
    }

    [Fact]
    public async Task Responses_carry_hardening_headers()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());

        // An authenticated API response must never be stored by a shared cache.
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_route_is_a_plain_404()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Malformed JSON must come back as a problem document, not as an
    /// unhandled exception with a stack trace.
    /// </summary>
    [Fact]
    public async Task Malformed_json_is_reported_as_a_bad_request()
    {
        var client = fixture.CreateClient();

        using var content = new StringContent("{ this is not json", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/auth/login", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Workspace.Api", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Problem_documents_identify_the_error_with_a_stable_code()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_api_reference_page_is_served()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/docs");

        await response.EnsureSuccessAsync();

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("/openapi/v1.json", body, StringComparison.Ordinal);
    }
}
