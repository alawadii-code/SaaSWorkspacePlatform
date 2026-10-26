using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Workspace.Application.Abstractions;
using Workspace.Application.Audit;
using Workspace.Application.Auth;
using Workspace.Application.Invitations;
using Workspace.Application.Members;
using Workspace.Application.Organizations;
using Workspace.Application.Projects;
using Workspace.Application.Tasks;

namespace Workspace.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case services, validators and the job handlers that run in the worker.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<AuthService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<TaskService>();
        services.AddScoped<AuditQueryService>();

        services.AddScoped<IJobHandler, SendInvitationEmailHandler>();

        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(ServiceLifetime.Singleton);

        return services;
    }
}
