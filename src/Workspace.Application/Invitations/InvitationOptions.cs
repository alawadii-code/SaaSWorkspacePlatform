using System.ComponentModel.DataAnnotations;

namespace Workspace.Application.Invitations;

/// <summary>Invitation settings, bound from configuration and validated at startup so a misconfigured deployment fails immediately instead of sending broken links.</summary>
public sealed class InvitationOptions
{
    public const string SectionName = "Invitations";

    /// <summary>How long an invitation can be redeemed for.</summary>
    [Range(1, 90)]
    public int LifetimeDays { get; set; } = 7;

    /// <summary>Accept-link template; <c>{token}</c> is replaced w/ the plaintext token.</summary>
    [Required]
    public string AcceptUrlTemplate { get; set; } = "http://localhost:5173/invitations/accept?token={token}";

    /// <summary>How long to wait before the worker treats an invitation whose email was nvr queued as undelivered and retries it.</summary>
    [Range(10, 3600)]
    public int ReconcileAfterSeconds { get; set; } = 60;

    public TimeSpan Lifetime => TimeSpan.FromDays(LifetimeDays);

    public TimeSpan ReconcileAfter => TimeSpan.FromSeconds(ReconcileAfterSeconds);

    public string BuildAcceptUrl(string token) =>
        AcceptUrlTemplate.Replace("{token}", Uri.EscapeDataString(token), StringComparison.Ordinal);
}
