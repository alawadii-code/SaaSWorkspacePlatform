using System.Net;
using System.Net.Http.Json;
using Workspace.Application.Common;
using Workspace.Application.Members;
using Workspace.Application.Projects;
using Workspace.Application.Tasks;
using Workspace.Domain.Authorization;
using Workspace.IntegrationTests.Fixtures;
using Xunit;

namespace Workspace.IntegrationTests;

/// <summary>The guarantee the brief calls "no cross-organization data access", asserted from the outside: two tenants exist, and the second one's owner — a fully authenticated, highly privileged user — can reach nothing of the first's.</summary>
[Collection(ApiCollection.Name)]
public class TenantIsolationTests(ApiFixture fixture)
{
    [Fact]
    public async Task An_outsider_cannot_read_the_organization()
    {
        var insider = await fixture.CreateUserAsync("insider");
        var organizationId = await insider.CreateOrganizationAsync("Insider Co");

        var outsider = await fixture.CreateUserAsync("outsider");
        await outsider.CreateOrganizationAsync("Outsider Co");

        var response = await outsider.Client.GetAsync($"/api/organizations/{organizationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_outsider_cannot_read_a_project_by_id()
    {
        var insider = await fixture.CreateUserAsync("insider");
        var insiderOrg = await insider.CreateOrganizationAsync("Insider Co");
        var projectId = await insider.CreateProjectAsync(insiderOrg, "Secret Launch");

        var outsider = await fixture.CreateUserAsync("outsider");
        var outsiderOrg = await outsider.CreateOrganizationAsync("Outsider Co");

        // Through the other tenant's own organization route.
        var viaOwnOrg = await outsider.Client.GetAsync($"/api/organizations/{outsiderOrg}/projects/{projectId}");
        Assert.Equal(HttpStatusCode.NotFound, viaOwnOrg.StatusCode);

        // And by addressing the owning organization directly.
        var viaOtherOrg = await outsider.Client.GetAsync($"/api/organizations/{insiderOrg}/projects/{projectId}");
        Assert.Equal(HttpStatusCode.NotFound, viaOtherOrg.StatusCode);
    }

    [Fact]
    public async Task Project_lists_contain_only_the_caller_organization()
    {
        var first = await fixture.CreateUserAsync("first");
        var firstOrg = await first.CreateOrganizationAsync("First Co");
        await first.CreateProjectAsync(firstOrg, "First Project");

        var second = await fixture.CreateUserAsync("second");
        var secondOrg = await second.CreateOrganizationAsync("Second Co");
        await second.CreateProjectAsync(secondOrg, "Second Project");

        var response = await second.Client.GetAsync($"/api/organizations/{secondOrg}/projects");
        await response.EnsureSuccessAsync();

        var page = await response.Content.ReadFromJsonAsync<PagedResult<ProjectDto>>(ApiScenarios.Json);

        Assert.Equal(1, page!.TotalCount);
        Assert.Equal("Second Project", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task Task_lists_contain_only_the_caller_organization()
    {
        var first = await fixture.CreateUserAsync("first");
        var firstOrg = await first.CreateOrganizationAsync("First Co");
        var firstProject = await first.CreateProjectAsync(firstOrg);
        await first.CreateTaskAsync(firstOrg, firstProject, "Their task");

        var second = await fixture.CreateUserAsync("second");
        var secondOrg = await second.CreateOrganizationAsync("Second Co");
        var secondProject = await second.CreateProjectAsync(secondOrg);
        await second.CreateTaskAsync(secondOrg, secondProject, "Our task");

        var response = await second.Client.GetAsync($"/api/organizations/{secondOrg}/tasks");
        await response.EnsureSuccessAsync();

        var page = await response.Content.ReadFromJsonAsync<PagedResult<TaskDto>>(ApiScenarios.Json);

        Assert.Equal("Our task", Assert.Single(page!.Items).Title);
    }

    /// <summary>Filtering by another tenant's project id must not act as a back door around the tenant filter.</summary>
    [Fact]
    public async Task Filtering_tasks_by_another_tenants_project_returns_nothing()
    {
        var first = await fixture.CreateUserAsync("first");
        var firstOrg = await first.CreateOrganizationAsync("First Co");
        var firstProject = await first.CreateProjectAsync(firstOrg);
        await first.CreateTaskAsync(firstOrg, firstProject, "Their task");

        var second = await fixture.CreateUserAsync("second");
        var secondOrg = await second.CreateOrganizationAsync("Second Co");

        var response = await second.Client.GetAsync(
            $"/api/organizations/{secondOrg}/tasks?projectId={firstProject}");

        await response.EnsureSuccessAsync();

        var page = await response.Content.ReadFromJsonAsync<PagedResult<TaskDto>>(ApiScenarios.Json);

        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task A_task_cannot_be_created_in_another_tenants_project()
    {
        var first = await fixture.CreateUserAsync("first");
        var firstOrg = await first.CreateOrganizationAsync("First Co");
        var firstProject = await first.CreateProjectAsync(firstOrg);

        var second = await fixture.CreateUserAsync("second");
        var secondOrg = await second.CreateOrganizationAsync("Second Co");

        var response = await second.Client.PostAsJsonAsync(
            $"/api/organizations/{secondOrg}/projects/{firstProject}/tasks",
            new { title = "Injected task", priority = "High" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_task_cannot_be_assigned_to_someone_outside_the_organization()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Assignment Co");
        var projectId = await owner.CreateProjectAsync(organizationId);
        var taskId = await owner.CreateTaskAsync(organizationId, projectId);

        var stranger = await fixture.CreateUserAsync("stranger");

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}/assign",
            new { assigneeUserId = stranger.UserId },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("validation_failed", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Member_lists_contain_only_the_caller_organization()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Members Co");
        await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var outsiderOwner = await fixture.CreateUserAsync("outsider");
        var outsiderOrg = await outsiderOwner.CreateOrganizationAsync("Outsider Co");

        var response = await outsiderOwner.Client.GetAsync($"/api/organizations/{outsiderOrg}/members");
        await response.EnsureSuccessAsync();

        var page = await response.Content.ReadFromJsonAsync<PagedResult<MemberDto>>(ApiScenarios.Json);

        Assert.Equal(1, page!.TotalCount);
        Assert.Equal(outsiderOwner.Email, Assert.Single(page.Items).Email);
    }

    [Fact]
    public async Task An_outsider_cannot_invite_into_another_organization()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Target Co");

        var outsider = await fixture.CreateUserAsync("outsider");

        var response = await outsider.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/invitations",
            new { email = "someone@example.test", role = OrganizationRole.Admin },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_outsider_cannot_read_another_organizations_audit_trail()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Audited Co");
        await owner.CreateProjectAsync(organizationId);

        var outsider = await fixture.CreateUserAsync("outsider");

        var response = await outsider.Client.GetAsync($"/api/organizations/{organizationId}/audit-logs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Leaving_an_organization_removes_access_to_it()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Revoked Co");
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var before = await member.Client.GetAsync($"/api/organizations/{organizationId}");
        await before.EnsureSuccessAsync();

        var leave = await member.Client.PostAsync(
            $"/api/organizations/{organizationId}/members/me/leave",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        var after = await member.Client.GetAsync($"/api/organizations/{organizationId}");

        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Removing_a_member_revokes_their_access_immediately()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Eviction Co");
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var removal = await owner.Client.DeleteAsync(
            $"/api/organizations/{organizationId}/members/{member.UserId}");

        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        var response = await member.Client.GetAsync($"/api/organizations/{organizationId}/projects");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
