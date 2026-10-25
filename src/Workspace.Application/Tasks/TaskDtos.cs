using FluentValidation;
using Workspace.Domain;

namespace Workspace.Application.Tasks;

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    DateTimeOffset? DueAt);

/// <summary>
/// Full replacement of a task's own fields. Assignment is a separate operation
/// so that "field absent" and "clear the assignee" cannot be confused.
/// </summary>
public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    TaskState Status,
    TaskPriority Priority,
    DateTimeOffset? DueAt);

/// <summary>Assigns a task, or clears the assignee when the id is null.</summary>
public sealed record AssignTaskRequest(Guid? AssigneeUserId);

public sealed record TaskDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Title,
    string? Description,
    TaskState Status,
    TaskPriority Priority,
    Guid? AssigneeUserId,
    string? AssigneeName,
    Guid CreatedByUserId,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public sealed class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
    }
}
