# ==============================================================================
# Multi-Stage Dockerfile: ControlPlane.Api (.NET 10 Web API)
# ==============================================================================

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Cache project restore layers
COPY src/Aspire/ControlPlane.ServiceDefaults/ControlPlane.ServiceDefaults.csproj src/Aspire/ControlPlane.ServiceDefaults/
COPY src/ControlPlane.Api/ControlPlane.Api.csproj src/ControlPlane.Api/
RUN dotnet restore src/ControlPlane.Api/ControlPlane.Api.csproj

# Copy project source trees
COPY src/Aspire/ControlPlane.ServiceDefaults/ src/Aspire/ControlPlane.ServiceDefaults/
COPY src/ControlPlane.Api/ src/ControlPlane.Api/

# Compile and publish optimized Release build
RUN dotnet publish src/ControlPlane.Api/ControlPlane.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore && \
    rm -rf /app/publish/runtimes/osx* /app/publish/runtimes/win* && \
    find /app/publish -name "*.pdb" -delete

# Stage 2: Hardened Production Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install minimal curl for health check probe and purge apt cache
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Copy published binaries from build stage
COPY --from=build /app/publish .

# Enforce execution under unprivileged non-root user (app, UID 1654)
USER app

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

# Periodic health check probe against /healthz
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "ControlPlane.Api.dll"]
