using FluentValidation;
using Workspace.Domain;

namespace Workspace.Application.Projects;

public sealed record CreateProjectRequest(string Name, string? Description);

public sealed record UpdateProjectRequest(string Name, string? Description, ProjectStatus Status);

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    ProjectStatus Status,
    int TaskCount,
    int OpenTaskCount,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ArchivedAt);

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MinimumLength(2).MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Status).IsInEnum();
    }
}
