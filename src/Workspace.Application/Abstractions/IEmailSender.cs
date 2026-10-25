namespace Workspace.Application.Abstractions;

/// <summary>An outbound email.</summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string HtmlBody);

/// <summary>
/// Sends email. SMTP in Docker Compose (delivered to Mailpit locally), and a
/// logging implementation for tests so no test run can send real mail.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
