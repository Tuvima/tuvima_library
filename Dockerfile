# ── Tuvima Library Engine ─────────────────────────────────────────────────────
# Multi-stage build for MediaEngine.Api (the Engine)
#
# Build:   docker build -t tuvima/engine .
# Run:     docker run -p 8080:8080 -v tuvima-data:/data -v tuvima-library:/library -v tuvima-watch:/watch tuvima/engine
# ──────────────────────────────────────────────────────────────────────────────

# ── Stage 1: Restore ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore

WORKDIR /src

# Copy solution, build props, and all csproj files first (layer caching)
COPY MediaEngine.slnx .
COPY global.json .
COPY Directory.Build.props .
COPY Directory.Packages.props .

# Copy all project files for restore
COPY src/MediaEngine.Domain/MediaEngine.Domain.csproj src/MediaEngine.Domain/
COPY src/MediaEngine.Storage/MediaEngine.Storage.csproj src/MediaEngine.Storage/
COPY src/MediaEngine.Intelligence/MediaEngine.Intelligence.csproj src/MediaEngine.Intelligence/
COPY src/MediaEngine.Processors/MediaEngine.Processors.csproj src/MediaEngine.Processors/
COPY src/MediaEngine.Providers/MediaEngine.Providers.csproj src/MediaEngine.Providers/
COPY src/MediaEngine.Ingestion/MediaEngine.Ingestion.csproj src/MediaEngine.Ingestion/
COPY src/MediaEngine.Identity/MediaEngine.Identity.csproj src/MediaEngine.Identity/
COPY src/MediaEngine.Api/MediaEngine.Api.csproj src/MediaEngine.Api/
COPY src/MediaEngine.Web/MediaEngine.Web.csproj src/MediaEngine.Web/

# Copy test project files (needed for solution restore)
COPY tests/MediaEngine.Domain.Tests/MediaEngine.Domain.Tests.csproj tests/MediaEngine.Domain.Tests/
COPY tests/MediaEngine.Ingestion.Tests/MediaEngine.Ingestion.Tests.csproj tests/MediaEngine.Ingestion.Tests/
COPY tests/MediaEngine.Intelligence.Tests/MediaEngine.Intelligence.Tests.csproj tests/MediaEngine.Intelligence.Tests/
COPY tests/MediaEngine.Processors.Tests/MediaEngine.Processors.Tests.csproj tests/MediaEngine.Processors.Tests/
COPY tests/MediaEngine.Storage.Tests/MediaEngine.Storage.Tests.csproj tests/MediaEngine.Storage.Tests/
COPY tests/MediaEngine.Providers.Tests/MediaEngine.Providers.Tests.csproj tests/MediaEngine.Providers.Tests/
COPY tests/MediaEngine.Api.Tests/MediaEngine.Api.Tests.csproj tests/MediaEngine.Api.Tests/

RUN dotnet restore

# ── Stage 2: Build & Publish ─────────────────────────────────────────────────
FROM restore AS build

COPY src/ src/
COPY tests/ tests/
COPY config.example/ config.example/

RUN dotnet publish src/MediaEngine.Api/MediaEngine.Api.csproj \
    -c Release \
    --no-restore \
    -o /app/publish

# ── Stage 3: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# SkiaSharp native dependencies for hero banner generation
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libfreetype6 && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Copy example config as default (user can override via volume mount)
COPY --from=build /src/config.example/ /app/config/

# Create directories for volumes
RUN mkdir -p /data /library /watch

# Configure ASP.NET to listen on 8080 (container convention)
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Point Engine paths to volume mount points
ENV MediaEngine__DatabasePath=/data/library.db
ENV MediaEngine__ConfigDirectory=/app/config
ENV MediaEngine__Security__LocalhostBypass=true

EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/system/status || exit 1

ENTRYPOINT ["dotnet", "MediaEngine.Api.dll"]
