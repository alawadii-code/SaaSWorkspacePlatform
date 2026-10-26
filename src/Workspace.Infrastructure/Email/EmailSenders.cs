using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using Workspace.Application.Abstractions;
using Workspace.Infrastructure.Options;

namespace Workspace.Infrastructure.Email;

/// <summary>Sends mail over SMTP.</summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        mime.Body = new MultipartAlternative
        {
            new TextPart(TextFormat.Plain) { Text = message.TextBody },
            new TextPart(TextFormat.Html) { Text = message.HtmlBody },
        };

        using var client = new SmtpClient
        {
            Timeout = settings.TimeoutSeconds * 1000,
        };

        var socketOptions = settings.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        logger.LogInformation("Delivered email to {Recipient} via {Host}.", message.To, settings.Host);
    }
}

/// <summary>Writes emails to the log instead of sending them.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Email (not sent, SMTP disabled) to {Recipient}: {Subject}\n{Body}",
            message.To,
            message.Subject,
            message.TextBody);

        return Task.CompletedTask;
    }
}
