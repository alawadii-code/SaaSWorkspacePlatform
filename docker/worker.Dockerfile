# syntax=docker/dockerfile:1

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

RUN dotnet restore src/Workspace.Worker/Workspace.Worker.csproj

COPY src/ src/

RUN dotnet publish src/Workspace.Worker/Workspace.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# The worker needs no web server, so it runs on the smaller runtime image.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY --from=build /app ./

USER $APP_UID

ENTRYPOINT ["dotnet", "Workspace.Worker.dll"]
