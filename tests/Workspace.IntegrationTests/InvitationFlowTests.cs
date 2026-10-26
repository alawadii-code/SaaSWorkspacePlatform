using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Application.Auth;
using Workspace.Application.Common;
using Workspace.Application.Invitations;
using Workspace.Domain;
using Workspace.Domain.Authorization;
using Workspace.Domain.Entities;
using Workspace.IntegrationTests.Fixtures;
using Xunit;

namespace Workspace.IntegrationTests;

/// <summary>The invitation lifecycle end to end, including the part that runs in the background: the API only queues the email, and the handler that sends it is executed here the way the worker would execute it.</summary>
[Collection(ApiCollection.Name)]
public class InvitationFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task Inviting_queues_a_job_rather_than_sending_inline()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Queued Co");

        fixture.Emails.Clear();

        var (invitationId, token) = await fixture.InviteAsync(
            owner,
            organizationId,
            $"invitee-{Guid.NewGuid():N}@example.test");

        // The request returned without touching SMTP.
        Assert.Empty(fixture.Emails.Sent);
        Assert.False(string.IsNullOrWhiteSpace(token));

        Assert.Contains(
            fixture.Queue.Enqueued,
            job => job.Type == JobTypes.SendInvitationEmail && job.Payload.Contains(invitationId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_api_never_returns_the_token_and_the_database_stores_only_its_hash()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Hashed Co");
        var email = $"invitee-{Guid.NewGuid():N}@example.test";

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/invitations",
            new { email, role = OrganizationRole.Member },
            ApiScenarios.Json);

        await response.EnsureSuccessAsync();

        var body = await response.Content.ReadAsStringAsync();
        var invitation = await response.Content.ReadFromJsonAsync<InvitationDto>(ApiScenarios.Json);
        var token = fixture.ReadInvitationToken(invitation!.Id);

        Assert.DoesNotContain(token, body, StringComparison.Ordinal);

        await fixture.ExecuteDbAsync(async db =>
        {
            var stored = await db.CrossTenant<Invitation>()
                .FirstAsync(candidate => candidate.Id == invitation.Id);

            Assert.NotEqual(token, stored.TokenHash);
            Assert.DoesNotContain(token, stored.TokenHash, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task The_worker_sends_an_email_containing_the_accept_link()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Delivery Co");
        var email = $"invitee-{Guid.NewGuid():N}@example.test";

        fixture.Emails.Clear();

        var (invitationId, token) = await fixture.InviteAsync(owner, organizationId, email);

        await fixture.DrainJobsAsync();

        var sent = fixture.Emails.LastTo(email);

        Assert.NotNull(sent);
        Assert.Contains("Delivery Co", sent.Subject, StringComparison.Ordinal);
        Assert.Contains(token, sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("/invitations/accept?token=", sent.TextBody, StringComparison.Ordinal);

        // Delivery state is recorded, so an admin can see what happened.
        var listed = await ListInvitationsAsync(owner, organizationId);
        var dto = Assert.Single(listed.Items, candidate => candidate.Id == invitationId);

        Assert.Equal(InvitationDeliveryStatus.Sent, dto.DeliveryStatus);
        Assert.Equal(1, dto.DeliveryAttempts);
        Assert.Null(dto.DeliveryError);
    }

    /// <summary>A failing mail server must not lose the invitation: the handler records the error, and the failure propagates so the worker's retry policy decides what happens next.</summary>
    [Fact]
    public async Task A_failed_send_is_recorded_and_surfaces_to_the_retry_policy()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Failure Co");
        var email = $"invitee-{Guid.NewGuid():N}@example.test";

        // Clear any backlog first, so the only job that fails is this one.
        await fixture.DrainJobsAsync();

        var (invitationId, _) = await fixture.InviteAsync(owner, organizationId, email);

        fixture.Emails.FailWith = new InvalidOperationException("SMTP server refused the connection.");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.DrainJobsAsync());
        }
        finally
        {
            fixture.Emails.FailWith = null;
        }

        var listed = await ListInvitationsAsync(owner, organizationId);
        var dto = Assert.Single(listed.Items, candidate => candidate.Id == invitationId);

        Assert.Equal(InvitationDeliveryStatus.Failed, dto.DeliveryStatus);
        Assert.Equal(1, dto.DeliveryAttempts);
        Assert.Contains("SMTP", dto.DeliveryError!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accepting_creates_a_membership_with_the_invited_role()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Welcome Co");

        var invitee = await fixture.CreateUserAsync("invitee");
        var (_, token) = await fixture.InviteAsync(owner, organizationId, invitee.Email, OrganizationRole.Admin);

        var accept = await invitee.Client.PostAsJsonAsync(
            "/api/invitations/accept",
            new { token },
            ApiScenarios.Json);

        await accept.EnsureSuccessAsync();

        var result = await accept.Content.ReadFromJsonAsync<AcceptInvitationResult>(ApiScenarios.Json);

        Assert.Equal(organizationId, result!.OrganizationId);
        Assert.Equal(OrganizationRole.Admin, result.Role);

        var me = await invitee.Client.GetFromJsonAsync<MeResponse>("/api/auth/me", ApiScenarios.Json);
        var membership = Assert.Single(me!.Organizations);

        Assert.Equal(organizationId, membership.OrganizationId);
        Assert.Equal(OrganizationRole.Admin, membership.Role);
    }

    [Fact]
    public async Task A_token_cannot_be_used_twice()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Single Use Co");

        var invitee = await fixture.CreateUserAsync("invitee");
        var (_, token) = await fixture.InviteAsync(owner, organizationId, invitee.Email);

        var first = await invitee.Client.PostAsJsonAsync("/api/invitations/accept", new { token }, ApiScenarios.Json);
        await first.EnsureSuccessAsync();

        var second = await invitee.Client.PostAsJsonAsync("/api/invitations/accept", new { token }, ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>A forwarded invitation link must be useless to anyone but its recipient, or one leaked email becomes tenant access.</summary>
    [Fact]
    public async Task Another_account_cannot_redeem_someone_elses_invitation()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Targeted Co");

        var intended = await fixture.CreateUserAsync("intended");
        var stranger = await fixture.CreateUserAsync("stranger");

        var (_, token) = await fixture.InviteAsync(owner, organizationId, intended.Email);

        var response = await stranger.Client.PostAsJsonAsync(
            "/api/invitations/accept",
            new { token },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var me = await stranger.Client.GetFromJsonAsync<MeResponse>("/api/auth/me", ApiScenarios.Json);

        Assert.Empty(me!.Organizations);
    }

    [Fact]
    public async Task Accepting_requires_authentication()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Anonymous Co");
        var (_, token) = await fixture.InviteAsync(owner, organizationId, $"invitee-{Guid.NewGuid():N}@example.test");

        var anonymous = fixture.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/invitations/accept", new { token }, ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_is_anonymous_and_returns_only_what_the_accept_screen_needs()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Preview Co");
        var email = $"invitee-{Guid.NewGuid():N}@example.test";

        var (_, token) = await fixture.InviteAsync(owner, organizationId, email, OrganizationRole.Viewer);

        var anonymous = fixture.CreateClient();
        var response = await anonymous.GetAsync($"/api/invitations/preview?token={Uri.EscapeDataString(token)}");

        await response.EnsureSuccessAsync();

        var preview = await response.Content.ReadFromJsonAsync<InvitationPreviewDto>(ApiScenarios.Json);

        Assert.Equal("Preview Co", preview!.OrganizationName);
        Assert.Equal(email, preview.Email);
        Assert.Equal(OrganizationRole.Viewer, preview.Role);
        Assert.True(preview.IsValid);
        Assert.Null(preview.Reason);

        // No organization id, member list or inviter details leak here.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(organizationId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(owner.Email, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_rejects_an_unknown_token()
    {
        var anonymous = fixture.CreateClient();

        var response = await anonymous.GetAsync("/api/invitations/preview?token=not-a-real-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoking_invalidates_a_link_that_is_already_in_an_inbox()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Revoked Invite Co");

        var invitee = await fixture.CreateUserAsync("invitee");
        var (invitationId, token) = await fixture.InviteAsync(owner, organizationId, invitee.Email);

        var revoke = await owner.Client.DeleteAsync(
            $"/api/organizations/{organizationId}/invitations/{invitationId}");

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var accept = await invitee.Client.PostAsJsonAsync(
            "/api/invitations/accept",
            new { token },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact]
    public async Task Resending_issues_a_new_token_and_retires_the_old_one()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Resend Co");

        var invitee = await fixture.CreateUserAsync("invitee");
        var (invitationId, firstToken) = await fixture.InviteAsync(owner, organizationId, invitee.Email);

        var resend = await owner.Client.PostAsync(
            $"/api/organizations/{organizationId}/invitations/{invitationId}/resend",
            content: null);

        await resend.EnsureSuccessAsync();

        var secondToken = fixture.ReadInvitationToken(invitationId);

        Assert.NotEqual(firstToken, secondToken);

        var withOldToken = await invitee.Client.PostAsJsonAsync(
            "/api/invitations/accept",
            new { token = firstToken },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.NotFound, withOldToken.StatusCode);

        var withNewToken = await invitee.Client.PostAsJsonAsync(
            "/api/invitations/accept",
            new { token = secondToken },
            ApiScenarios.Json);

        await withNewToken.EnsureSuccessAsync();
    }

    [Fact]
    public async Task An_expired_invitation_cannot_be_redeemed()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Expiry Co");

        var invitee = await fixture.CreateUserAsync("invitee");
        var (invitationId, token) = await fixture.InviteAsync(owner, organizationId, invitee.Email);

        await fixture.ExecuteDbAsync(async db =>
        {
            var invitation = await db.CrossTenant<Invitation>().FirstAsync(i => i.Id == invitationId);
            invitation.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        });

        var accept = await invitee.Client.PostAsJsonAsync(
            "/api/invitations/accept",
            new { token },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);

        var preview = await fixture.CreateClient()
            .GetFromJsonAsync<InvitationPreviewDto>(
                $"/api/invitations/preview?token={Uri.EscapeDataString(token)}",
                ApiScenarios.Json);

        Assert.False(preview!.IsValid);
        Assert.Contains("expired", preview.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_pending_invitations_for_the_same_address_are_refused()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Duplicate Invite Co");
        var email = $"invitee-{Guid.NewGuid():N}@example.test";

        await fixture.InviteAsync(owner, organizationId, email);

        var second = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/invitations",
            new { email, role = OrganizationRole.Member },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Inviting_an_existing_member_is_refused()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Already In Co");
        var member = await fixture.AddMemberAsync(owner, organizationId, OrganizationRole.Member);

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/invitations",
            new { email = member.Email, role = OrganizationRole.Admin },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_invitation_can_be_reissued_after_the_first_is_accepted()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Reissue Co");

        var invitee = await fixture.CreateUserAsync("invitee");
        var (_, token) = await fixture.InviteAsync(owner, organizationId, invitee.Email);

        var accept = await invitee.Client.PostAsJsonAsync("/api/invitations/accept", new { token }, ApiScenarios.Json);
        await accept.EnsureSuccessAsync();

        var leave = await invitee.Client.PostAsync(
            $"/api/organizations/{organizationId}/members/me/leave",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);

        var reinvite = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/invitations",
            new { email = invitee.Email, role = OrganizationRole.Viewer },
            ApiScenarios.Json);

        await reinvite.EnsureSuccessAsync();
    }

    [Fact]
    public async Task Invitations_can_be_filtered_by_status()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Filtered Invite Co");

        var accepted = await fixture.CreateUserAsync("accepted");
        var (_, acceptedToken) = await fixture.InviteAsync(owner, organizationId, accepted.Email);

        var accept = await accepted.Client.PostAsJsonAsync(
            "/api/invitations/accept",
            new { token = acceptedToken },
            ApiScenarios.Json);

        await accept.EnsureSuccessAsync();

        await fixture.InviteAsync(owner, organizationId, $"pending-{Guid.NewGuid():N}@example.test");

        var pending = await ListInvitationsAsync(owner, organizationId, InvitationStatus.Pending);
        var acceptedList = await ListInvitationsAsync(owner, organizationId, InvitationStatus.Accepted);

        Assert.All(pending.Items, item => Assert.Equal(InvitationStatus.Pending, item.Status));
        Assert.All(acceptedList.Items, item => Assert.Equal(InvitationStatus.Accepted, item.Status));
        Assert.NotEmpty(pending.Items);
        Assert.NotEmpty(acceptedList.Items);
    }

    [Fact]
    public async Task Invalid_email_addresses_are_rejected_before_anything_is_queued()
    {
        var owner = await fixture.CreateUserAsync("owner");
        var organizationId = await owner.CreateOrganizationAsync("Validation Invite Co");

        var queuedBefore = fixture.Queue.Enqueued.Count;

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/invitations",
            new { email = "not-an-email", role = OrganizationRole.Member },
            ApiScenarios.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(queuedBefore, fixture.Queue.Enqueued.Count);
    }

    private static async Task<PagedResult<InvitationDto>> ListInvitationsAsync(
        TestUser user,
        Guid organizationId,
        InvitationStatus? status = null)
    {
        var url = $"/api/organizations/{organizationId}/invitations"
            + (status is null ? string.Empty : $"?status={status}");

        var response = await user.Client.GetAsync(url);
        await response.EnsureSuccessAsync();

        return (await response.Content.ReadFromJsonAsync<PagedResult<InvitationDto>>(ApiScenarios.Json))!;
    }
}
