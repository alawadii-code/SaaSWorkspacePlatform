using FluentValidation;
using Workspace.Domain.Authorization;

namespace Workspace.Application.Members;

public sealed record MemberDto(
    Guid MembershipId,
    Guid UserId,
    string Email,
    string DisplayName,
    OrganizationRole Role,
    DateTimeOffset JoinedAt,
    bool IsYou);

public sealed record ChangeMemberRoleRequest(OrganizationRole Role);

public sealed class ChangeMemberRoleRequestValidator : AbstractValidator<ChangeMemberRoleRequest>
{
    public ChangeMemberRoleRequestValidator() =>
        RuleFor(x => x.Role)
            .IsInEnum()
            .WithMessage("Role must be one of Viewer, Member, Admin or Owner.");
}
