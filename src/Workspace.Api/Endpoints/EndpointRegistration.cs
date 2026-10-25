using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Workspace.Api.Endpoints;

public static class EndpointRegistration
{
    /// <summary>
    /// Maps every route. Reading this method top to bottom is meant to be the
    /// fastest way to understand the API's shape.
    /// </summary>
    public static WebApplication MapWorkspaceEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapAuthEndpoints();

        // Organization routes, plus the tenant-scoped group that members,
        // invitations, projects, tasks and audit logs hang off.
        api.MapOrganizationEndpoints();

        // Invitation redemption cannot live under an organization route: the
        // invitee has no membership until they accept.
        api.MapInvitationRedemptionEndpoints();

        app.MapOperationalEndpoints();

        return app;
    }

    private static void MapOperationalEndpoints(this WebApplication app)
    {
        // Liveness: is the process answering? Deliberately has no dependency
        // checks, so a database blip cannot get a healthy API restarted.
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
            .WithTags("Operations")
            .WithSummary("Liveness probe")
            .AllowAnonymous();

        // Readiness: should this instance receive traffic?
        app.MapGet("/health/ready", async (HealthCheckService healthChecks, CancellationToken cancellationToken) =>
            {
                var report = await healthChecks.CheckHealthAsync(
                    registration => registration.Tags.Contains("ready"),
                    cancellationToken);

                var payload = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.ToDictionary(
                        entry => entry.Key,
                        entry => new
                        {
                            status = entry.Value.Status.ToString(),
                            description = entry.Value.Description,
                        }),
                };

                return report.Status == HealthStatus.Unhealthy
                    ? Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.Ok(payload);
            })
            .WithTags("Operations")
            .WithSummary("Readiness probe")
            .AllowAnonymous();

        // OpenAPI document, generated from the endpoint metadata above.
        app.MapOpenApi();

        app.MapGet("/", () => Results.Ok(new
            {
                name = "Workspace API",
                documentation = "/docs",
                openapi = "/openapi/v1.json",
                health = "/health",
            }))
            .WithTags("Operations")
            .ExcludeFromDescription()
            .AllowAnonymous();

        app.MapGet("/docs", () => Results.Content(ReferenceDocumentation, "text/html"))
            .WithTags("Operations")
            .ExcludeFromDescription()
            .AllowAnonymous();
    }

    /// <summary>
    /// A one-file API reference: Scalar rendering the generated OpenAPI
    /// document. Loaded from a CDN rather than taking another NuGet
    /// dependency, and this is the one response allowed a relaxed CSP.
    /// </summary>
    private const string ReferenceDocumentation =
        """
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Workspace API reference</title>
            <meta http-equiv="Content-Security-Policy"
                  content="default-src 'self'; script-src 'self' https://cdn.jsdelivr.net 'unsafe-inline'; style-src 'self' https://cdn.jsdelivr.net 'unsafe-inline'; font-src https://cdn.jsdelivr.net; img-src 'self' data:; connect-src 'self'" />
          </head>
          <body>
            <div id="app"></div>
            <script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script>
            <script>
              Scalar.createApiReference('#app', {
                url: '/openapi/v1.json',
                theme: 'default'
              });
            </script>
          </body>
        </html>
        """;
}
