using Workspace.Application.Invitations;
using Workspace.Domain.Authorization;
using Xunit;

namespace Workspace.UnitTests.Invitations;

public class InvitationEmailTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Includes_the_accept_link_in_both_bodies()
    {
        const string acceptUrl = "https://app.example.com/invitations/accept?token=abc123";

        var email = InvitationEmail.Build(
            "invitee@example.com",
            "Acme Corp",
            "Awadi",
            OrganizationRole.Member,
            acceptUrl,
            Expiry);

        Assert.Equal("invitee@example.com", email.To);
        Assert.Contains(acceptUrl, email.TextBody, StringComparison.Ordinal);
        Assert.Contains(acceptUrl, email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Subject_names_the_organization() =>
        Assert.Contains(
            "Acme Corp",
            InvitationEmail.Build("a@b.com", "Acme Corp", "Awadi", OrganizationRole.Admin, "https://x.test", Expiry).Subject,
            StringComparison.Ordinal);

    [Theory]
    [InlineData(OrganizationRole.Owner, "an owner")]
    [InlineData(OrganizationRole.Admin, "an admin")]
    [InlineData(OrganizationRole.Member, "a member")]
    [InlineData(OrganizationRole.Viewer, "a viewer")]
    public void States_the_role_in_words(OrganizationRole role, string expected)
    {
        var email = InvitationEmail.Build("a@b.com", "Acme", "Awadi", role, "https://x.test", Expiry);

        Assert.Contains(expected, email.TextBody, StringComparison.Ordinal);
    }

    /// <summary>Organization and inviter names are user-controlled, and this email is delivered to someone else, so they must not be able to carry markup into the recipient's mail client.</summary>
    [Fact]
    public void Escapes_html_in_user_supplied_names()
    {
        var email = InvitationEmail.Build(
            "invitee@example.com",
            "<script>alert('xss')</script>Acme",
            "<img src=x onerror=alert(1)>",
            OrganizationRole.Member,
            "https://app.example.com/accept?token=t",
            Expiry);

        Assert.DoesNotContain("<script>", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror=", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void States_that_the_link_is_single_use_and_expiring()
    {
        var email = InvitationEmail.Build("a@b.com", "Acme", "Awadi", OrganizationRole.Member, "https://x.test", Expiry);

        Assert.Contains("works once", email.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires", email.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tells_an_unexpecting_recipient_they_can_ignore_it() =>
        Assert.Contains(
            "ignore",
            InvitationEmail.Build("a@b.com", "Acme", "Awadi", OrganizationRole.Member, "https://x.test", Expiry).TextBody,
            StringComparison.OrdinalIgnoreCase);
}
