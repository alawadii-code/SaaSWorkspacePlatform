using System.Text.Encodings.Web;
using Workspace.Application.Abstractions;
using Workspace.Domain.Authorization;

namespace Workspace.Application.Invitations;

/// <summary>Builds the invitation email.</summary>
public static class InvitationEmail
{
    public static EmailMessage Build(
        string recipientEmail,
        string organizationName,
        string invitedByName,
        OrganizationRole role,
        string acceptUrl,
        DateTimeOffset expiresAt)
    {
        var subject = $"You have been invited to {organizationName}";

        var text =
            $"""
            {invitedByName} invited you to join {organizationName} as {RoleLabel(role)}.

            Accept the invitation:
            {acceptUrl}

            This link works once and expires on {expiresAt:u}.
            If you were not expecting this invitation, you can ignore this email.
            """;

        var encodedOrganization = HtmlEncoder.Default.Encode(organizationName);
        var encodedInviter = HtmlEncoder.Default.Encode(invitedByName);
        var encodedRole = HtmlEncoder.Default.Encode(RoleLabel(role));
        var encodedUrl = HtmlEncoder.Default.Encode(acceptUrl);

        var html =
            $"""
            <!doctype html>
            <html lang="en">
              <body style="font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; color: #1f2933; line-height: 1.6;">
                <h2 style="margin-bottom: 0.25rem;">Join {encodedOrganization}</h2>
                <p>{encodedInviter} invited you to join <strong>{encodedOrganization}</strong> as <strong>{encodedRole}</strong>.</p>
                <p>
                  <a href="{encodedUrl}"
                     style="display: inline-block; padding: 0.6rem 1.1rem; background: #2f6feb; color: #ffffff; border-radius: 6px; text-decoration: none;">
                    Accept invitation
                  </a>
                </p>
                <p style="font-size: 0.875rem; color: #616e7c;">
                  This link works once and expires on {expiresAt:u}.
                  If you were not expecting this invitation, you can ignore this email.
                </p>
              </body>
            </html>
            """;

        return new EmailMessage(recipientEmail, subject, text, html);
    }

    private static string RoleLabel(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => "an owner",
        OrganizationRole.Admin => "an admin",
        OrganizationRole.Member => "a member",
        OrganizationRole.Viewer => "a viewer",
        _ => "a member",
    };
}
