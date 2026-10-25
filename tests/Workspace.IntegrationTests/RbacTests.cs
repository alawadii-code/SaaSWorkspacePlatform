using System.Net;
using System.Net.Http.Json;
using Workspace.Application.Auth;
using Workspace.Application.Members;
using Workspace.Domain.Authorization;
using Workspace.IntegrationTests.Fixtures;
using Xunit;

namespace Workspace.IntegrationTests;

/// <summary>
/// Role-based permissions as enforced over HTTP. The matrix itself is covered
/// by unit tests; these assert that the server — not the client — applies it,
/// including the row-level rule for a member's own tasks.
/// </summary>
[Collection(ApiCollection.Name)]
public class RbacTests(ApiFixture fixture)
{
    [Fact]
    public async Task Viewer_can_read_but_not_write()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Viewer Co");
        var projectId = await owner.CreateProjectAsync(organizationId);

        var viewer = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Viewer);

        var read = await viewer.Client.GetAsync($"/api/organizations/{organizationId}/projects");
        await read.EnsureSuccessAsync();

        var createProject = await viewer.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name = "Viewer project" },
            ApiScenarios.Json);

        var createTask = await viewer.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects/{projectId}/tasks",
            new { title = "Viewer task", priority = "Low" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, createProject.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createTask.StatusCode);
        Assert.Equal("forbidden", await createTask.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Member_can_create_tasks_but_not_projects()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Member Co");
        var projectId = await owner.CreateProjectAsync(organizationId);

        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var createProject = await member.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name = "Member project" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, createProject.StatusCode);

        var taskId = await member.CreateTaskAsync(organizationId, projectId, "Member's own task");

        Assert.NotEqual(Guid.Empty, taskId);
    }

    [Fact]
    public async Task Member_can_update_their_own_task_but_not_someone_elses()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Ownership Co");
        var projectId = await owner.CreateProjectAsync(organizationId);

        var author = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member, "author");
        var other = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member, "other");

        var taskId = await author.CreateTaskAsync(organizationId, projectId, "Authored task");

        var body = new
        {
            title = "Authored task, edited",
            description = (string?)null,
            status = "InProgress",
            priority = "High",
            dueAt = (DateTimeOffset?)null,
        };

        var byAuthor = await author.Client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}",
            body,
            ApiScenarios.Json);

        await byAuthor.EnsureSuccessAsync();

        var byOther = await other.Client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}",
            body,
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, byOther.StatusCode);
    }

    /// <summary>
    /// A member may also edit a task assigned to them, which is the other half
    /// of what "own" means.
    /// </summary>
    [Fact]
    public async Task Member_can_update_a_task_assigned_to_them()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Assignee Co");
        var projectId = await owner.CreateProjectAsync(organizationId);

        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);
        var taskId = await owner.CreateTaskAsync(organizationId, projectId, "Assigned task", member.UserId);

        var response = await member.Client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}",
            new
            {
                title = "Assigned task",
                description = "Picked it up",
                status = "InProgress",
                priority = "Medium",
                dueAt = (DateTimeOffset?)null,
            },
            ApiScenarios.Json);

        await response.EnsureSuccessAsync();
    }

    [Fact]
    public async Task Admin_can_update_any_task()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Admin Co");
        var projectId = await owner.CreateProjectAsync(organizationId);

        var admin = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Admin);
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var taskId = await member.CreateTaskAsync(organizationId, projectId, "Member task");

        var response = await admin.Client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}",
            new
            {
                title = "Reprioritised by admin",
                description = (string?)null,
                status = "Blocked",
                priority = "Urgent",
                dueAt = (DateTimeOffset?)null,
            },
            ApiScenarios.Json);

        await response.EnsureSuccessAsync();
    }

    [Fact]
    public async Task Only_admins_and_above_can_read_the_audit_trail()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Audit Rbac Co");

        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);
        var admin = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Admin);

        var asMember = await member.Client.GetAsync($"/api/organizations/{organizationId}/audit-logs");
        var asAdmin = await admin.Client.GetAsync($"/api/organizations/{organizationId}/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, asMember.StatusCode);
        await asAdmin.EnsureSuccessAsync();
    }

    [Fact]
    public async Task Only_owners_can_delete_the_organization()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Deletable Co");
        var admin = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Admin);

        var byAdmin = await admin.Client.DeleteAsync(
            $"/api/organizations/{organizationId}?confirm=deletable-co");

        Assert.Equal(HttpStatusCode.Forbidden, byAdmin.StatusCode);

        var byOwner = await owner.Client.DeleteAsync(
            $"/api/organizations/{organizationId}?confirm=deletable-co");

        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_organization_requires_the_slug_as_confirmation()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Careful Co");

        var withoutConfirmation = await owner.Client.DeleteAsync($"/api/organizations/{organizationId}");
        var withWrongConfirmation = await owner.Client.DeleteAsync(
            $"/api/organizations/{organizationId}?confirm=not-the-slug");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutConfirmation.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withWrongConfirmation.StatusCode);

        var stillThere = await owner.Client.GetAsync($"/api/organizations/{organizationId}");
        await stillThere.EnsureSuccessAsync();
    }

    [Fact]
    public async Task An_admin_cannot_invite_someone_as_owner()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Escalation Co");
        var admin = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Admin);

        var response = await admin.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/invitations",
            new { email = $"newowner-{Guid.NewGuid():N}@example.test", role = OrganizationRole.Owner },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_change_an_owners_role()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Hierarchy Co");
        var admin = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Admin);

        var response = await admin.Client.PatchAsJsonAsync(
            $"/api/organizations/{organizationId}/members/{owner.UserId}/role",
            new { role = OrganizationRole.Member },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_cannot_promote_anyone_to_owner()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Promotion Co");
        var admin = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Admin);
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var response = await admin.Client.PatchAsJsonAsync(
            $"/api/organizations/{organizationId}/members/{member.UserId}/role",
            new { role = OrganizationRole.Owner },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_can_change_their_own_role()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Self Co");

        var response = await owner.Client.PatchAsJsonAsync(
            $"/api/organizations/{organizationId}/members/{owner.UserId}/role",
            new { role = OrganizationRole.Admin },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_last_owner_cannot_leave()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Lonely Co");

        var response = await owner.Client.PostAsync(
            $"/api/organizations/{organizationId}/members/me/leave",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_can_leave_once_another_owner_exists()
    {
        var first = await fixture.CreateUserAsync("first-owner");
        var organizationId = await first.CreateOrganizationAsync("Succession Co");
        var second = await fixture.AddMemberAsync(first, organizationId, OrganizationRole.Member, "second-owner");

        var promote = await first.Client.PatchAsJsonAsync(
            $"/api/organizations/{organizationId}/members/{second.UserId}/role",
            new { role = OrganizationRole.Owner },
            ApiScenarios.Json);

        await promote.EnsureSuccessAsync();

        var leave = await first.Client.PostAsync(
            $"/api/organizations/{organizationId}/members/me/leave",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
    }

    [Fact]
    public async Task A_promotion_takes_effect_without_a_new_token()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Live Role Co");
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var before = await member.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name = "Too early" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        var promote = await owner.Client.PatchAsJsonAsync(
            $"/api/organizations/{organizationId}/members/{member.UserId}/role",
            new { role = OrganizationRole.Admin },
            ApiScenarios.Json);

        await promote.EnsureSuccessAsync();

        // Same access token as before: roles are read from the database per
        // request, so the change is effective immediately.
        var after = await member.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name = "Now allowed" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Created, after.StatusCode);
    }

    [Fact]
    public async Task Me_reports_the_permissions_that_match_the_role()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Permissions Co");
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var response = await member.Client.GetAsync("/api/auth/me");
        await response.EnsureSuccessAsync();

        var me = await response.Content.ReadFromJsonAsync<MeResponse>(ApiScenarios.Json);
        var membership = Assert.Single(me!.Organizations);

        Assert.Equal(OrganizationRole.Member, membership.Role);
        Assert.Contains(nameof(Permission.TaskCreate), membership.Permissions);
        Assert.DoesNotContain(nameof(Permission.ProjectCreate), membership.Permissions);
        Assert.DoesNotContain(nameof(Permission.AuditRead), membership.Permissions);
    }

    [Fact]
    public async Task Members_can_be_listed_with_their_roles()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Roster Co");
        await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Admin);
        await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Viewer);

        var response = await owner.Client.GetAsync($"/api/organizations/{organizationId}/members");
        await response.EnsureSuccessAsync();

        var page = await response.Content.ReadFromJsonAsync<Workspace.Application.Common.PagedResult<MemberDto>>(
            ApiScenarios.Json);

        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(OrganizationRole.Owner, page.Items[0].Role);
        Assert.True(page.Items[0].IsYou);
    }
}
