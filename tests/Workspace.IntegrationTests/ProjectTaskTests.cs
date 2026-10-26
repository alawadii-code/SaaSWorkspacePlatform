using System.Net;
using System.Net.Http.Json;
using Workspace.Application.Common;
using Workspace.Application.Projects;
using Workspace.Application.Tasks;
using Workspace.Domain;
using Workspace.Domain.Authorization;
using Workspace.IntegrationTests.Fixtures;
using Xunit;

namespace Workspace.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProjectTaskTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_created_project_can_be_listed_and_fetched()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Projects Co");

        var create = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name = "Mobile App", description = "Ship the first release" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<ProjectDto>(ApiScenarios.Json);

        Assert.Equal("Mobile App", created!.Name);
        Assert.Equal("mobile-app", created.Slug);
        Assert.Equal(ProjectStatus.Active, created.Status);
        Assert.Equal(0, created.TaskCount);

        var fetched = await owner.Client.GetFromJsonAsync<ProjectDto>(
            $"/api/organizations/{organizationId}/projects/{created.Id}",
            ApiScenarios.Json);

        Assert.Equal(created.Id, fetched!.Id);
    }

    /// <summary>Slugs are unique per tenant, not globally: two organizations can both have a project with the same name and the same handle.</summary>
    [Fact]
    public async Task The_same_project_name_is_available_in_every_organization()
    {
        var first = await fixture.CreateUserAsync("first");
        var firstOrg = await first.CreateOrganizationAsync("First Slug Co");

        var second = await fixture.CreateUserAsync("second");
        var secondOrg = await second.CreateOrganizationAsync("Second Slug Co");

        var firstProject = await first.Client.PostAsJsonAsync(
            $"/api/organizations/{firstOrg}/projects",
            new { name = "Shared Name" },
            ApiScenarios.Json);

        var secondProject = await second.Client.PostAsJsonAsync(
            $"/api/organizations/{secondOrg}/projects",
            new { name = "Shared Name" },
            ApiScenarios.Json);

        await firstProject.EnsureSuccessAsync();
        await secondProject.EnsureSuccessAsync();

        var left = await firstProject.Content.ReadFromJsonAsync<ProjectDto>(ApiScenarios.Json);
        var right = await secondProject.Content.ReadFromJsonAsync<ProjectDto>(ApiScenarios.Json);

        Assert.Equal("shared-name", left!.Slug);
        Assert.Equal("shared-name", right!.Slug);
    }

    [Fact]
    public async Task A_duplicate_name_within_one_organization_gets_a_distinct_slug()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Dedupe Co");

        var first = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name = "Same Name" },
            ApiScenarios.Json);

        var second = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name = "Same Name" },
            ApiScenarios.Json);

        await first.EnsureSuccessAsync();
        await second.EnsureSuccessAsync();

        var left = await first.Content.ReadFromJsonAsync<ProjectDto>(ApiScenarios.Json);
        var right = await second.Content.ReadFromJsonAsync<ProjectDto>(ApiScenarios.Json);

        Assert.Equal("same-name", left!.Slug);
        Assert.StartsWith("same-name-", right!.Slug, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Archiving_a_project_blocks_new_tasks()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Archive Co");
        var projectId = await owner.CreateProjectAsync(organizationId, "To Archive");

        var archive = await owner.Client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/projects/{projectId}",
            new { name = "To Archive", description = (string?)null, status = ProjectStatus.Archived },
            ApiScenarios.Json);

        await archive.EnsureSuccessAsync();

        var archived = await archive.Content.ReadFromJsonAsync<ProjectDto>(ApiScenarios.Json);

        Assert.Equal(ProjectStatus.Archived, archived!.Status);
        Assert.NotNull(archived.ArchivedAt);

        var addTask = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects/{projectId}/tasks",
            new { title = "Too late", priority = "Low" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Conflict, addTask.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_project_removes_its_tasks()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Cascade Co");
        var projectId = await owner.CreateProjectAsync(organizationId);
        var taskId = await owner.CreateTaskAsync(organizationId, projectId);

        var delete = await owner.Client.DeleteAsync(
            $"/api/organizations/{organizationId}/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var task = await owner.Client.GetAsync($"/api/organizations/{organizationId}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.NotFound, task.StatusCode);
    }

    [Fact]
    public async Task Completing_a_task_records_when_it_was_completed()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Completion Co");
        var projectId = await owner.CreateProjectAsync(organizationId);
        var taskId = await owner.CreateTaskAsync(organizationId, projectId, "Finish the draft");

        var complete = await owner.Client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}",
            new
            {
                title = "Finish the draft",
                description = (string?)null,
                status = TaskState.Done,
                priority = TaskPriority.Medium,
                dueAt = (DateTimeOffset?)null,
            },
            ApiScenarios.Json);

        await complete.EnsureSuccessAsync();

        var done = await complete.Content.ReadFromJsonAsync<TaskDto>(ApiScenarios.Json);

        Assert.Equal(TaskState.Done, done!.Status);
        Assert.NotNull(done.CompletedAt);

        var reopen = await owner.Client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}",
            new
            {
                title = "Finish the draft",
                description = (string?)null,
                status = TaskState.InProgress,
                priority = TaskPriority.Medium,
                dueAt = (DateTimeOffset?)null,
            },
            ApiScenarios.Json);

        await reopen.EnsureSuccessAsync();

        var reopened = await reopen.Content.ReadFromJsonAsync<TaskDto>(ApiScenarios.Json);

        Assert.Null(reopened!.CompletedAt);
    }

    [Fact]
    public async Task Tasks_can_be_assigned_and_unassigned()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Assign Co");
        var projectId = await owner.CreateProjectAsync(organizationId);
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);
        var taskId = await owner.CreateTaskAsync(organizationId, projectId);

        var assign = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}/assign",
            new { assigneeUserId = member.UserId },
            ApiScenarios.Json);

        await assign.EnsureSuccessAsync();

        var assigned = await assign.Content.ReadFromJsonAsync<TaskDto>(ApiScenarios.Json);

        Assert.Equal(member.UserId, assigned!.AssigneeUserId);
        Assert.Equal(ApiScenarios.DisplayName, assigned.AssigneeName);

        var unassign = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{taskId}/assign",
            new { assigneeUserId = (Guid?)null },
            ApiScenarios.Json);

        await unassign.EnsureSuccessAsync();

        var unassigned = await unassign.Content.ReadFromJsonAsync<TaskDto>(ApiScenarios.Json);

        Assert.Null(unassigned!.AssigneeUserId);
    }

    /// <summary>Removing someone must not leave tasks pointing at a user who can no longer see them.</summary>
    [Fact]
    public async Task Removing_a_member_clears_their_assignments()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Unassign Co");
        var projectId = await owner.CreateProjectAsync(organizationId);
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);
        var taskId = await owner.CreateTaskAsync(organizationId, projectId, "Assigned work", member.UserId);

        var remove = await owner.Client.DeleteAsync(
            $"/api/organizations/{organizationId}/members/{member.UserId}");

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var task = await owner.Client.GetFromJsonAsync<TaskDto>(
            $"/api/organizations/{organizationId}/tasks/{taskId}",
            ApiScenarios.Json);

        Assert.Null(task!.AssigneeUserId);
    }

    [Fact]
    public async Task Tasks_can_be_filtered_by_status_and_assignee()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Filter Co");
        var projectId = await owner.CreateProjectAsync(organizationId);
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        await owner.CreateTaskAsync(organizationId, projectId, "Unassigned work");
        var assignedId = await owner.CreateTaskAsync(organizationId, projectId, "Assigned work", member.UserId);

        var byAssignee = await owner.Client.GetFromJsonAsync<PagedResult<TaskDto>>(
            $"/api/organizations/{organizationId}/tasks?assigneeUserId={member.UserId}",
            ApiScenarios.Json);

        Assert.Equal(assignedId, Assert.Single(byAssignee!.Items).Id);

        var byStatus = await owner.Client.GetFromJsonAsync<PagedResult<TaskDto>>(
            $"/api/organizations/{organizationId}/tasks?status={TaskState.Done}",
            ApiScenarios.Json);

        Assert.Empty(byStatus!.Items);

        var bySearch = await owner.Client.GetFromJsonAsync<PagedResult<TaskDto>>(
            $"/api/organizations/{organizationId}/tasks?search=unassigned",
            ApiScenarios.Json);

        Assert.Equal("Unassigned work", Assert.Single(bySearch!.Items).Title);
    }

    [Fact]
    public async Task Page_size_is_capped_regardless_of_what_the_client_asks_for()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Paging Co");

        var page = await owner.Client.GetFromJsonAsync<PagedResult<ProjectDto>>(
            $"/api/organizations/{organizationId}/projects?page=0&pageSize=100000",
            ApiScenarios.Json);

        Assert.Equal(1, page!.Page);
        Assert.Equal(PageRequest.MaxPageSize, page.PageSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    public async Task Short_titles_are_rejected_with_a_field_error(string title)
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync($"Validation Co {Guid.NewGuid():N}");
        var projectId = await owner.CreateProjectAsync(organizationId);

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects/{projectId}/tasks",
            new { title, priority = "Low" },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("title", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_over_long_description_is_rejected()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Long Text Co");

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/projects",
            new { name = "Verbose", description = new string('x', 5_000) },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Project_counts_reflect_open_and_total_tasks()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Counting Co");
        var projectId = await owner.CreateProjectAsync(organizationId);

        var firstTask = await owner.CreateTaskAsync(organizationId, projectId, "First task");
        await owner.CreateTaskAsync(organizationId, projectId, "Second task");

        var complete = await owner.Client.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/tasks/{firstTask}",
            new
            {
                title = "First task",
                description = (string?)null,
                status = TaskState.Done,
                priority = TaskPriority.Medium,
                dueAt = (DateTimeOffset?)null,
            },
            ApiScenarios.Json);

        await complete.EnsureSuccessAsync();

        var project = await owner.Client.GetFromJsonAsync<ProjectDto>(
            $"/api/organizations/{organizationId}/projects/{projectId}",
            ApiScenarios.Json);

        Assert.Equal(2, project!.TaskCount);
        Assert.Equal(1, project.OpenTaskCount);
    }
}
