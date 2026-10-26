namespace Workspace.Application.Abstractions;

/// <summary>An outbound email.</summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string HtmlBody);

/// <summary>Sends email.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
