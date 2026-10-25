using Workspace.Api;
using Workspace.Api.Endpoints;
using Workspace.Api.Http;
using Workspace.Application;
using Workspace.Infrastructure;
using Workspace.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration, builder.Environment);

var app = builder.Build();

// Exception handling wraps everything else, so a failure anywhere downstream
// still answers with a problem document rather than an empty 500.
app.UseExceptionHandler();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

// Behind nginx or another proxy, trust the forwarded scheme and client address
// so rate-limit partitions and audit rows record the real caller.
app.UseForwardedHeaders();

app.UseCors(CorsOptions.PolicyName);
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapWorkspaceEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
{
    await DatabaseMigrator.MigrateAsync(app.Services);
}

await app.RunAsync();
