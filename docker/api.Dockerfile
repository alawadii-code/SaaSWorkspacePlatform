# syntax=docker/dockerfile:1

# Build stage. Project files are copied first so the restore layer is cached
# until a dependency actually changes, not on every source edit.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props Workspace.sln ./
COPY src/Workspace.Domain/Workspace.Domain.csproj src/Workspace.Domain/
COPY src/Workspace.Application/Workspace.Application.csproj src/Workspace.Application/
COPY src/Workspace.Infrastructure/Workspace.Infrastructure.csproj src/Workspace.Infrastructure/
COPY src/Workspace.Api/Workspace.Api.csproj src/Workspace.Api/
COPY src/Workspace.Worker/Workspace.Worker.csproj src/Workspace.Worker/
COPY tests/Workspace.UnitTests/Workspace.UnitTests.csproj tests/Workspace.UnitTests/
COPY tests/Workspace.IntegrationTests/Workspace.IntegrationTests.csproj tests/Workspace.IntegrationTests/

RUN dotnet restore src/Workspace.Api/Workspace.Api.csproj

COPY src/ src/

RUN dotnet publish src/Workspace.Api/Workspace.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# Runtime stage: the ASP.NET image only, with no SDK and no source.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is here only so Compose and orchestrators can probe /health.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY --from=build /app ./

# APP_UID is the non-root user the .NET base images define. Nothing in this
# container needs to write to disk or bind a privileged port.
USER $APP_UID

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Workspace.Api.dll"]
