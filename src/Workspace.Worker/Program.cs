using Workspace.Application;
using Workspace.Application.Abstractions;
using Workspace.Infrastructure;
using Workspace.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// No HTTP context here: background work acts as the system.
builder.Services.AddSingleton<ICurrentUser, SystemCurrentUser>();

builder.Services.AddHostedService<JobProcessor>();
builder.Services.AddHostedService<MaintenanceWorker>();

var host = builder.Build();

await host.RunAsync();
