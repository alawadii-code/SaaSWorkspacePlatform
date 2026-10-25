using FluentValidation;
using Workspace.Domain;
using Workspace.Domain.Authorization;

namespace Workspace.Application.Invitations;

public sealed record CreateInvitationRequest(string Email, OrganizationRole Role);

public sealed record InvitationDto(
    Guid Id,
    string Email,
    OrganizationRole Role,
    InvitationStatus Status,
    InvitationDeliveryStatus DeliveryStatus,
    int DeliveryAttempts,
    string? DeliveryError,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AcceptedAt);

/// <summary>
/// What an invitee is shown before signing in, resolved from the token in
/// their link. Carries no member list, project data or ids beyond what the
/// accept screen needs.
/// </summary>
public sealed record InvitationPreviewDto(
    string OrganizationName,
    string Email,
    OrganizationRole Role,
    DateTimeOffset ExpiresAt,
    bool IsValid,
    string? Reason);

/// <summary>Result of redeeming an invitation.</summary>
public sealed record AcceptInvitationResult(
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationSlug,
    OrganizationRole Role);

/// <summary>
/// Queued job arguments. The plaintext token travels here because it is never
/// stored: the database holds only its hash, so the email is the single place
/// it exists at rest.
/// </summary>
public sealed record SendInvitationEmailPayload(Guid InvitationId, string Token);

public sealed class CreateInvitationRequestValidator : AbstractValidator<CreateInvitationRequest>
{
    public CreateInvitationRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();

        RuleFor(x => x.Role)
            .IsInEnum()
            .WithMessage("Role must be one of Viewer, Member, Admin or Owner.");
    }
}
