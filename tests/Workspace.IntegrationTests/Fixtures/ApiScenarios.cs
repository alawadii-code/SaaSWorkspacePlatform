using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Workspace.Application.Abstractions;
using Workspace.Application.Auth;
using Workspace.Application.Invitations;
using Workspace.Application.Organizations;
using Workspace.Domain.Authorization;

namespace Workspace.IntegrationTests.Fixtures;

/// <summary>A registered, signed-in user with their own HTTP client.</summary>
public sealed record TestUser(HttpClient Client, Guid UserId, string Email, string Password, string AccessToken);

/// <summary>Scenario helpers so each test reads as the behaviour it asserts rather than as a sequence of HTTP calls.</summary>
public static class ApiScenarios
{
    /// <summary>A passphrase that satisfies the password policy and shares no fragment with the test display name or email addresses.</summary>
    public const string Password = "correct-horse-battery-staple-9";

    public const string DisplayName = "Sam Rivers";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Registers a new user and returns a client authenticated as them.</summary>
    public static async Task<TestUser> CreateUserAsync(this ApiFixture fixture, string label = "user")
    {
        var client = fixture.CreateClient();
        var email = $"{label}-{Guid.NewGuid():N}@example.test";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = Password, displayName = DisplayName },
            Json);

        await response.EnsureSuccessAsync();

        var auth = await response.Content.ReadFromJsonAsync<AuthResult>(Json);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return new TestUser(client, auth.User.Id, email, Password, auth.AccessToken);
    }

    /// <summary>Creates an organization owned by <paramref name="owner"/>.</summary>
    public static async Task<Guid> CreateOrganizationAsync(this TestUser owner, string name = "Acme Corp")
    {
        var response = await owner.Client.PostAsJsonAsync("/api/organizations", new { name }, Json);

        await response.EnsureSuccessAsync();

        var organization = await response.Content.ReadFromJsonAsync<OrganizationDto>(Json);

        return organization!.Id;
    }

    /// <summary>Invites an address and returns the invitation id together with the plaintext token.</summary>
    public static async Task<(Guid InvitationId, string Token)> InviteAsync(
        this ApiFixture fixture,
        TestUser inviter,
        Guid organizationId,
        string email,
        OrganizationRole role = OrganizationRole.Member)
    {
        var response = await inviter.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/invitations",
            new { email, role },
            Json);

        await response.EnsureSuccessAsync();

        var invitation = await response.Content.ReadFromJsonAsync<InvitationDto>(Json);

        return (invitation!.Id, fixture.ReadInvitationToken(invitation.Id));
    }

    /// <summary>Latest queued token for an invitation.</summary>
    public static string ReadInvitationToken(this ApiFixture fixture, Guid invitationId)
    {
        var payloads = fixture.Queue.Enqueued
            .Where(job => job.Type == JobTypes.SendInvitationEmail)
            .Select(job => JsonSerializer.Deserialize<SendInvitationEmailPayload>(job.Payload, Json)!)
            .Where(payload => payload.InvitationId == invitationId)
            .ToList();

        if (payloads.Count == 0)
        {
            throw new InvalidOperationException($"No invitation email was queued for {invitationId}.");
        }

        return payloads[^1].Token;
    }

    /// <summary>Registers a user, invites them at <paramref name="role"/> and accepts, returning the new member.</summary>
    public static async Task<TestUser> AddMemberAsync(
        this ApiFixture fixture,
        TestUser owner,
        Guid organizationId,
        OrganizationRole role,
        string? label = null)
    {
        var member = await fixture.CreateUserAsync(label ?? role.ToString().ToLowerInvariant());

        var (_, token) = await fixture.InviteAsync(owner, organizationId, member.Email, role);

        var accept = await member.Client.PostAsJsonAsync("/api/invitations/accept", new { token }, Json);

        await accept.EnsureSuccessAsync();

        return member;
    }

    /// <summary>Runs every queued job through its handler, the way the worker would.</summary>
    public static async Task<int> DrainJobsAsync(this ApiFixture fixture)
    {
        var queue = fixture.Queue;
        var processed = 0;

        while (await queue.DequeueAsync(TimeSpan.Zero) is { } message)
        {
            using var scope = fixture.Services.CreateScope();

            var handler = scope.ServiceProvider
                .GetServices<IJobHandler>()
                .First(candidate => candidate.JobType == message.Type);

            await handler.HandleAsync(message, CancellationToken.None);
            await queue.AcknowledgeAsync(message);

            processed++;
        }

        return processed;
    }

    /// <summary>Creates a project and returns its id.</summary>
    public static async Task<Guid> CreateProjectAsync(
        this TestUser user,
        Guid organizationId,
        string name = "Website Redesign")
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name, description = (string?)null },
            Json);

        await response.EnsureSuccessAsync();

        var project = await response.Content.ReadFromJsonAsync<Workspace.Application.Projects.ProjectDto>(Json);

        return project!.Id;
    }

    /// <summary>Creates a task and returns its id.</summary>
    public static async Task<Guid> CreateTaskAsync(
        this TestUser user,
        Guid organizationId,
        Guid projectId,
        string title = "Write the brief",
        Guid? assigneeUserId = null)
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects/{projectId}/tasks",
            new
            {
                title,
                description = (string?)null,
                priority = "Medium",
                assigneeUserId,
                dueAt = (DateTimeOffset?)null,
            },
            Json);

        await response.EnsureSuccessAsync();

        var task = await response.Content.ReadFromJsonAsync<Workspace.Application.Tasks.TaskDto>(Json);

        return task!.Id;
    }

    /// <summary>Runs an action against the application's own DbContext, for setting up states the API deliberately cannot produce (an invitation whose expiry has passed) and for asserting database-level guarantees.</summary>
    public static async Task ExecuteDbAsync(
        this ApiFixture fixture,
        Func<Workspace.Infrastructure.Persistence.WorkspaceDbContext, Task> action)
    {
        using var scope = fixture.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<Workspace.Infrastructure.Persistence.WorkspaceDbContext>();

        await action(db);
    }

    /// <summary>Like <c>EnsureSuccessStatusCode</c>, but puts the response body in the failure message — a problem document explains far more than "422".</summary>
    public static async Task EnsureSuccessAsync(this HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();

        throw new InvalidOperationException(
            $"{(int)response.StatusCode} {response.StatusCode} from {response.RequestMessage?.RequestUri}: {body}");
    }

    /// <summary>Reads the <c>code</c> extension from a problem document.</summary>
    public static async Task<string?> ReadErrorCodeAsync(this HttpResponseMessage response)
    {
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>();

        return document is not null && document.RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }
}
