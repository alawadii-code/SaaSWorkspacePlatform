using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;
using Workspace.Domain.Entities;

namespace Workspace.Api.Http;

/// <summary>
/// Resolves which organization a request is acting on, and the caller's role
/// in it, from the route and the authenticated user.
/// </summary>
/// <remarks>
/// <para>
/// Applied to the whole <c>/api/organizations/{organizationId}</c> group, so
/// no endpoint inside it can forget it. The role is read from the database on
/// every request rather than taken from the token, which is what makes a role
/// change or a removal take effect immediately.
/// </para>
/// <para>
/// A caller with no membership gets 404, not 403. Answering "forbidden" would
/// confirm that the organization exists, turning this endpoint into an oracle
/// for probing other tenants' ids.
/// </para>
/// </remarks>
public sealed class TenantResolutionFilter : IEndpointFilter
{
    public const string RouteParameterName = "organizationId";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        // Resolved per request, not injected: minimal APIs construct an
        // endpoint filter once, from the root provider, so a constructor
        // parameter here would be a scoped service captured by a singleton —
        // which the container rightly refuses.
        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<IWorkspaceDb>();
        var tenantSetter = services.GetRequiredService<ITenantContextSetter>();
        var currentUser = services.GetRequiredService<ICurrentUser>();

        var userId = currentUser.RequireUserId();

        if (!context.HttpContext.Request.RouteValues.TryGetValue(RouteParameterName, out var raw) ||
            !Guid.TryParse(raw?.ToString(), out var organizationId))
        {
            throw new NotFoundException("Organization");
        }

        // Cross-tenant by necessity — this query is what establishes the
        // tenant — and constrained to the caller's own user id.
        var role = await db.CrossTenant<Membership>()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (role is null)
        {
            throw new NotFoundException("Organization");
        }

        tenantSetter.Set(organizationId, userId, role.Value);

        return await next(context);
    }
}

/// <summary>
/// Validates a request body with its FluentValidation validator before the
/// handler runs, so handlers never see malformed input.
/// </summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
        {
            throw new InvalidOperationException(
                $"Endpoint declares validation for {typeof(TRequest).Name} but has no such argument.");
        }

        // Resolved from the request scope for the same reason as above.
        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<TRequest>>();

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (!result.IsValid)
        {
            throw new ValidationFailedException(
                result.Errors
                    .GroupBy(failure => ToCamelCase(failure.PropertyName), StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(failure => failure.ErrorMessage).ToArray(),
                        StringComparer.Ordinal));
        }

        return await next(context);
    }

    /// <summary>Matches the JSON casing the client sent, so errors map to fields.</summary>
    private static string ToCamelCase(string propertyName) =>
        string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0])
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}

/// <summary>Marks an endpoint's required permission, for documentation.</summary>
public sealed record RequiredPermissionMetadata(Permission Permission);

public static class EndpointFilterExtensions
{
    /// <summary>
    /// Requires a permission before the handler runs.
    /// </summary>
    /// <remarks>
    /// The use-case service checks the same permission again. That duplication
    /// is deliberate: the filter gives a cheap, uniform rejection at the edge,
    /// and the service-level check means a new endpoint that forgets the
    /// filter is still not an open door.
    /// </remarks>
    public static RouteHandlerBuilder RequirePermission(
        this RouteHandlerBuilder builder,
        Permission permission) =>
        builder
            .AddEndpointFilter(async (context, next) =>
            {
                var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
                tenant.Require(permission);

                return await next(context);
            })
            .WithMetadata(new RequiredPermissionMetadata(permission))
            .WithDescription($"Requires the `{permission}` permission.")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

    /// <summary>Validates the request body of type <typeparamref name="TRequest"/>.</summary>
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder) =>
        builder
            .AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);
}
