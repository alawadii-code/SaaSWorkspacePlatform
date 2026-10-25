using Workspace.Application.Abstractions;

namespace Workspace.IntegrationTests.Fixtures;

/// <summary>
/// Records emails instead of sending them, so tests can assert on what a
/// recipient would actually receive — and so no test run can deliver mail.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly Lock gate = new();
    private readonly List<EmailMessage> sent = [];

    /// <summary>When set, <see cref="SendAsync"/> throws, to exercise retries.</summary>
    public Exception? FailWith { get; set; }

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (gate)
            {
                return [.. sent];
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            return Task.FromException(FailWith);
        }

        lock (gate)
        {
            sent.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>The most recent message to an address, or null.</summary>
    public EmailMessage? LastTo(string recipient)
    {
        lock (gate)
        {
            return sent.LastOrDefault(message =>
                string.Equals(message.To, recipient, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            sent.Clear();
        }
    }
}
