using FluentValidation;
using Workspace.Domain.Authorization;

namespace Workspace.Application.Organizations;

public sealed record CreateOrganizationRequest(string Name);

public sealed record UpdateOrganizationRequest(string Name);

public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    OrganizationRole CallerRole,
    IReadOnlyList<string> CallerPermissions,
    int MemberCount,
    int ProjectCount,
    DateTimeOffset CreatedAt);

public sealed class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(100);
}

public sealed class UpdateOrganizationRequestValidator : AbstractValidator<UpdateOrganizationRequest>
{
    public UpdateOrganizationRequestValidator() =>
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(100);
}
