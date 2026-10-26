using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workspace.Application.Audit;
using Workspace.Application.Common;
using Workspace.Domain.Audit;
using Workspace.Domain.Authorization;
using Workspace.Domain.Entities;
using Workspace.IntegrationTests.Fixtures;
using Xunit;

namespace Workspace.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuditLogTests(ApiFixture fixture)
{
    [Fact]
    public async Task Actions_are_recorded_with_actor_and_context()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Trail Co");
        var projectId = await owner.CreateProjectAsync(organizationId, "Audited Project");

        var page = await ReadTrailAsync(owner, organizationId);

        var entry = Assert.Single(
            page.Items,
            item => item.Action == AuditActions.ProjectCreated && item.ResourceId == projectId.ToString());

        Assert.Equal(AuditResources.Project, entry.ResourceType);
        Assert.Equal(owner.UserId, entry.ActorUserId);
        Assert.Equal(owner.Email, entry.ActorEmail);
        Assert.NotNull(entry.Metadata);
        Assert.Equal("Audited Project", entry.Metadata!.Value.GetProperty("name").GetString());

        // The organization's own creation is recorded too.
        Assert.Contains(page.Items, item => item.Action == AuditActions.OrganizationCreated);
    }

    [Fact]
    public async Task A_role_change_records_what_changed()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Role Trail Co");
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var promote = await owner.Client.PatchAsJsonAsync(
            $"/api/organizations/{organizationId}/members/{member.UserId}/role",
            new { role = OrganizationRole.Admin },
            ApiScenarios.Json);

        await promote.EnsureSuccessAsync();

        var page = await ReadTrailAsync(owner, organizationId, AuditActions.MemberRoleChanged);
        var entry = Assert.Single(page.Items);

        Assert.Equal("Member", entry.Metadata!.Value.GetProperty("from").GetString());
        Assert.Equal("Admin", entry.Metadata.Value.GetProperty("to").GetString());
    }

    [Fact]
    public async Task The_trail_is_scoped_to_one_organization()
    {
        var first = await fixture.CreateUserAsync("first");
        var firstOrg = await first.CreateOrganizationAsync("First Trail Co");
        await first.CreateProjectAsync(firstOrg, "First Secret");

        var second = await fixture.CreateUserAsync("second");
        var secondOrg = await second.CreateOrganizationAsync("Second Trail Co");

        var page = await ReadTrailAsync(second, secondOrg);

        // Nothing from the other tenant: not its actor, not its resources.
        Assert.All(page.Items, item => Assert.NotEqual(first.UserId, item.ActorUserId));
        Assert.All(page.Items, item => Assert.Equal(second.UserId, item.ActorUserId));
        Assert.DoesNotContain(
            page.Items,
            item => item.Metadata is not null &&
                item.Metadata.Value.ToString().Contains("First Secret", StringComparison.Ordinal));

        Assert.Contains(page.Items, item => item.Action == AuditActions.OrganizationCreated);
    }

    /// <summary>Sign-in events belong to an account, not a tenant, so they are recorded for operators but must not appear in any organization's trail.</summary>
    [Fact]
    public async Task Account_level_events_are_not_exposed_to_tenant_admins()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("No Logins Co");

        var client = fixture.CreateClient();

        await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = owner.Email, password = ApiScenarios.Password },
            ApiScenarios.Json);

        var page = await ReadTrailAsync(owner, organizationId);

        Assert.DoesNotContain(page.Items, item => item.Action == AuditActions.UserLoggedIn);
        Assert.DoesNotContain(page.Items, item => item.Action == AuditActions.UserRegistered);

        // They are recorded, just without an organization.
        await fixture.ExecuteDbAsync(async db =>
        {
            var accountEvents = await db.CrossTenant<AuditLog>()
                .Where(entry => entry.ActorUserId == owner.UserId && entry.OrganizationId == null)
                .Select(entry => entry.Action)
                .ToListAsync();

            Assert.Contains(AuditActions.UserRegistered, accountEvents);
            Assert.Contains(AuditActions.UserLoggedIn, accountEvents);
        });
    }

    [Fact]
    public async Task Failed_sign_ins_are_recorded_without_the_password()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var client = fixture.CreateClient();

        await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = owner.Email, password = "a-very-wrong-password" },
            ApiScenarios.Json);

        await fixture.ExecuteDbAsync(async db =>
        {
            var entry = await db.CrossTenant<AuditLog>()
                .Where(candidate => candidate.ActorUserId == owner.UserId && candidate.Action == AuditActions.UserLoginFailed)
                .OrderByDescending(candidate => candidate.CreatedAt)
                .FirstAsync();

            Assert.NotNull(entry.Metadata);
            Assert.Contains("bad_password", entry.Metadata, StringComparison.Ordinal);
            Assert.DoesNotContain("a-very-wrong-password", entry.Metadata, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task The_application_refuses_to_change_an_audit_entry()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Immutable App Co");
        await owner.CreateProjectAsync(organizationId);

        await fixture.ExecuteDbAsync(async db =>
        {
            var entry = await db.CrossTenant<AuditLog>()
                .FirstAsync(candidate => candidate.OrganizationId == organizationId);

            entry.Action = "tampered.by.code";

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

            Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>The database enforces the same rule, so bypassing the application layer does not help: this is the guarantee that makes the trail evidence rather than a convenience log.</summary>
    [Fact]
    public async Task The_database_rejects_updates_and_deletes_on_the_audit_table()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Immutable Db Co");
        await owner.CreateProjectAsync(organizationId);

        await fixture.ExecuteDbAsync(async db =>
        {
            var entryId = await db.CrossTenant<AuditLog>()
                .Where(candidate => candidate.OrganizationId == organizationId)
                .Select(candidate => candidate.Id)
                .FirstAsync();

            var update = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync(
                    "UPDATE audit_logs SET action = 'tampered.by.sql' WHERE id = {0}",
                    entryId));

            var delete = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("DELETE FROM audit_logs WHERE id = {0}", entryId));

            Assert.Contains("append-only", update.MessageText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("append-only", delete.MessageText, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Audit rows hold the organization id as plain data with no foreign key, precisely so the record of a deletion outlives what was deleted.</summary>
    [Fact]
    public async Task The_trail_survives_deletion_of_the_organization()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Ephemeral Co");
        await owner.CreateProjectAsync(organizationId);

        var delete = await owner.Client.DeleteAsync($"/api/organizations/{organizationId}?confirm=ephemeral-co");

        await delete.EnsureSuccessAsync();

        await fixture.ExecuteDbAsync(async db =>
        {
            var actions = await db.CrossTenant<AuditLog>()
                .Where(entry => entry.OrganizationId == organizationId)
                .Select(entry => entry.Action)
                .ToListAsync();

            Assert.Contains(AuditActions.OrganizationCreated, actions);
            Assert.Contains(AuditActions.ProjectCreated, actions);
            Assert.Contains(AuditActions.OrganizationDeleted, actions);
        });
    }

    [Fact]
    public async Task Entries_carry_the_requests_correlation_id()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Correlated Co");

        var correlationId = $"test-{Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/organizations/{organizationId}/projects")
        {
            Content = JsonContent.Create(new { name = "Correlated Project" }, options: ApiScenarios.Json),
        };

        request.Headers.Add("X-Correlation-Id", correlationId);

        var response = await owner.Client.SendAsync(request);
        await response.EnsureSuccessAsync();

        var page = await ReadTrailAsync(owner, organizationId, AuditActions.ProjectCreated);
        var entry = Assert.Single(page.Items);

        Assert.Equal(correlationId, entry.CorrelationId);
    }

    [Fact]
    public async Task The_actions_endpoint_lists_what_actually_happened()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Actions Co");
        var projectId = await owner.CreateProjectAsync(organizationId);
        await owner.CreateTaskAsync(organizationId, projectId);

        var actions = await owner.Client.GetFromJsonAsync<List<string>>(
            $"/api/organizations/{organizationId}/audit-logs/actions",
            ApiScenarios.Json);

        Assert.Contains(AuditActions.OrganizationCreated, actions!);
        Assert.Contains(AuditActions.ProjectCreated, actions);
        Assert.Contains(AuditActions.TaskCreated, actions);
        Assert.DoesNotContain(AuditActions.UserLoggedIn, actions);
        Assert.Equal(actions.OrderBy(action => action, StringComparer.Ordinal), actions);
    }

    [Fact]
    public async Task The_trail_can_be_filtered_by_actor_and_time()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Filtered Trail Co");
        var admin = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Admin);

        await admin.CreateProjectAsync(organizationId, "Admin's Project");

        var byAdmin = await owner.Client.GetFromJsonAsync<PagedResult<AuditLogDto>>(
            $"/api/organizations/{organizationId}/audit-logs?actorUserId={admin.UserId}",
            ApiScenarios.Json);

        Assert.NotEmpty(byAdmin!.Items);
        Assert.All(byAdmin.Items, item => Assert.Equal(admin.UserId, item.ActorUserId));

        var future = await owner.Client.GetFromJsonAsync<PagedResult<AuditLogDto>>(
            $"/api/organizations/{organizationId}/audit-logs?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}",
            ApiScenarios.Json);

        Assert.Empty(future!.Items);
    }

    [Fact]
    public async Task The_trail_is_newest_first()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Ordered Co");
        await owner.CreateProjectAsync(organizationId, "First");
        await owner.CreateProjectAsync(organizationId, "Second");

        var page = await ReadTrailAsync(owner, organizationId);

        var timestamps = page.Items.Select(item => item.CreatedAt).ToList();

        Assert.Equal(timestamps.OrderByDescending(timestamp => timestamp), timestamps);
    }

    private static async Task<PagedResult<AuditLogDto>> ReadTrailAsync(
        TestUser user,
        Guid organizationId,
        string? action = null)
    {
        var url = $"/api/organizations/{organizationId}/audit-logs"
            + (action is null ? string.Empty : $"?action={action}");

        var response = await user.Client.GetAsync(url);
        await response.EnsureSuccessAsync();

        return (await response.Content.ReadFromJsonAsync<PagedResult<AuditLogDto>>(ApiScenarios.Json))!;
    }
}
