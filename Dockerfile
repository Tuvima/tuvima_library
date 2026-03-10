# ── Tuvima Library ────────────────────────────────────────────────────────────
# Single container running both the Engine (port 8080) and Dashboard (port 8081)
#
# Build:   docker build -t tuvima/tuvima-library .
# Run:     docker run -p 8080:8080 -p 8081:8081 \
#            -v tuvima-data:/data -v /path/to/media:/library -v /path/to/watch:/watch \
#            tuvima/tuvima-library
# ──────────────────────────────────────────────────────────────────────────────

# ── Stage 1: Restore ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore

WORKDIR /src

# Copy solution, build props, and all csproj files first (layer caching)
COPY MediaEngine.slnx .
COPY global.json .
COPY Directory.Build.props .
COPY Directory.Packages.props .

COPY src/MediaEngine.Domain/MediaEngine.Domain.csproj src/MediaEngine.Domain/
COPY src/MediaEngine.Storage/MediaEngine.Storage.csproj src/MediaEngine.Storage/
COPY src/MediaEngine.Intelligence/MediaEngine.Intelligence.csproj src/MediaEngine.Intelligence/
COPY src/MediaEngine.Processors/MediaEngine.Processors.csproj src/MediaEngine.Processors/
COPY src/MediaEngine.Providers/MediaEngine.Providers.csproj src/MediaEngine.Providers/
COPY src/MediaEngine.Ingestion/MediaEngine.Ingestion.csproj src/MediaEngine.Ingestion/
COPY src/MediaEngine.Identity/MediaEngine.Identity.csproj src/MediaEngine.Identity/
COPY src/MediaEngine.Api/MediaEngine.Api.csproj src/MediaEngine.Api/
COPY src/MediaEngine.Web/MediaEngine.Web.csproj src/MediaEngine.Web/

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

# Publish Engine and Dashboard side by side
RUN dotnet publish src/MediaEngine.Api/MediaEngine.Api.csproj \
    -c Release --no-restore -o /app/engine

RUN dotnet publish src/MediaEngine.Web/MediaEngine.Web.csproj \
    -c Release --no-restore -o /app/dashboard

# ── Stage 3: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# OCI image metadata — used by Docker Hub, GHCR, and container tools
LABEL org.opencontainers.image.title="Tuvima Library" \
      org.opencontainers.image.description="The Private Universe Discovery & Media Engine — unified media intelligence platform for ebooks, audiobooks, comics, music, TV shows, and movies" \
      org.opencontainers.image.url="https://github.com/Tuvima/tuvima_library" \
      org.opencontainers.image.source="https://github.com/Tuvima/tuvima_library" \
      org.opencontainers.image.documentation="https://github.com/Tuvima/tuvima_library#readme" \
      org.opencontainers.image.licenses="AGPL-3.0-only" \
      org.opencontainers.image.vendor="Tuvima"

# SkiaSharp native dependencies for hero banner generation + curl for healthcheck
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libfreetype6 \
        curl && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy both published apps
COPY --from=build /app/engine ./engine/
COPY --from=build /app/dashboard ./dashboard/

# Copy example config as default (user can override via volume mount)
COPY --from=build /src/config.example/ /app/config/

# Copy entrypoint script
COPY docker/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Create directories for volumes
RUN mkdir -p /data /library /watch

# ── Engine configuration ──
ENV MediaEngine__DatabasePath=/data/library.db
ENV MediaEngine__ConfigDirectory=/app/config
ENV MediaEngine__Security__LocalhostBypass=true
ENV MediaEngine__Cors__AllowedOrigins__0=http://localhost:8081

# ── Dashboard configuration ──
# Dashboard talks to Engine over localhost inside the container
ENV Engine__BaseUrl=http://localhost:8080

ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080 8081

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/system/status && curl -f http://localhost:8081/ || exit 1

ENTRYPOINT ["/app/entrypoint.sh"]
