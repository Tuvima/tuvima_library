# ─────────────────────────────────────────────────────────────────────────────
# Tuvima Library — Docker multi-stage build
# Produces a single image containing both the Engine (port 61495) and
# the Dashboard (port 5016).
#
# Build:   docker build -t tuvima/library:latest .
# Run:     docker compose up   (see docker-compose.yml)
# ─────────────────────────────────────────────────────────────────────────────

# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy central package management and build props first (layer-cached separately
# from source so a code change doesn't bust the NuGet restore cache).
COPY Directory.Packages.props .
COPY Directory.Build.props .

# Copy every .csproj in the correct relative position for restore.
COPY src/MediaEngine.Domain/MediaEngine.Domain.csproj             src/MediaEngine.Domain/
COPY src/MediaEngine.Storage/MediaEngine.Storage.csproj           src/MediaEngine.Storage/
COPY src/MediaEngine.Intelligence/MediaEngine.Intelligence.csproj src/MediaEngine.Intelligence/
COPY src/MediaEngine.Processors/MediaEngine.Processors.csproj     src/MediaEngine.Processors/
COPY src/MediaEngine.Providers/MediaEngine.Providers.csproj       src/MediaEngine.Providers/
COPY src/MediaEngine.Ingestion/MediaEngine.Ingestion.csproj       src/MediaEngine.Ingestion/
COPY src/MediaEngine.Identity/MediaEngine.Identity.csproj         src/MediaEngine.Identity/
COPY src/MediaEngine.Api/MediaEngine.Api.csproj                   src/MediaEngine.Api/
COPY src/MediaEngine.Web/MediaEngine.Web.csproj                   src/MediaEngine.Web/

# Restore (cached until any .csproj changes).
RUN dotnet restore src/MediaEngine.Api/MediaEngine.Api.csproj
RUN dotnet restore src/MediaEngine.Web/MediaEngine.Web.csproj

# Copy remaining source and publish both projects.
COPY src/ src/
COPY config.example/ config.example/

RUN dotnet publish src/MediaEngine.Api/MediaEngine.Api.csproj \
    --configuration Release \
    --output /app/engine \
    --no-restore

RUN dotnet publish src/MediaEngine.Web/MediaEngine.Web.csproj \
    --configuration Release \
    --output /app/dashboard \
    --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# SkiaSharp native libraries need libfontconfig on Linux.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 \
 && rm -rf /var/lib/apt/lists/*

# Copy published output from build stage.
COPY --from=build /app/engine    ./engine
COPY --from=build /app/dashboard ./dashboard

# Copy example configs — the entrypoint seeds /config from these on first run.
COPY --from=build /src/config.example/ ./engine/config.example/

# Create named mount points so docker-compose volume declarations work.
RUN mkdir -p /watch /library /config /db

# Startup script that launches both processes.
COPY docker-entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# ── Ports ─────────────────────────────────────────────────────────────────────
# Engine API: 61495  |  Dashboard: 5016
EXPOSE 61495
EXPOSE 5016

ENTRYPOINT ["/entrypoint.sh"]
