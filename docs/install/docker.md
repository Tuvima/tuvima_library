---
title: "Install with Docker Compose"
summary: "Run Tuvima Library on a Docker host with persistent storage, a private Engine, and a health-checked Dashboard."
audience: "administrator"
category: "installation"
product_area: "deployment"
---

# Install with Docker Compose

The supported container deployment runs the Engine and Dashboard in one image. Only the Dashboard is published to the host on TCP port `5016`; the Engine remains on container loopback and must not be published or proxied.

## Before you start

Install Docker Engine with the Compose plugin, or Docker Desktop. Tuvima publishes Linux AMD64 and ARM64 images. The host needs enough storage for the catalogue, artwork, prepared playback variants, backups, and any Local AI models you choose to download.

Create or choose these host folders:

| Container path | Purpose | Required access |
| --- | --- | --- |
| `/watch` | Incoming catalogue media | Read; write only for managed intake |
| `/library` | Media managed by Tuvima | Read and write |
| `/config` | Non-secret configuration and data-protection keys | Read and write |
| `/db` | SQLite catalogue | Read and write |
| `/models` | Local AI model files | Read and write; allow several GB |
| `/artwork-cache` | Artwork, thumbnails, cache, and logs | Read and write |
| `/backups` | Backup archives and restore staging | Read and write |
| `/transcode` | Playback variants and temporary work | Read and write |

An existing library that Tuvima must not change should be mounted separately and read-only, then added in **Settings → Libraries** as an existing source. Do not point `/library` at user-owned originals unless Tuvima is allowed to manage that folder.

## Start Tuvima

1. Download `docker-compose.yml` from the release or repository.
2. Replace every example host path under `volumes` with a real absolute path on this server.
3. Set `TUVIMA_UID` and `TUVIMA_GID` to the numeric owner that can access those folders. Keep `TUVIMA_UMASK=0002` when group-writable files are appropriate.
4. Set `TZ` to an IANA timezone such as `America/Chicago` or `Europe/London`.
5. Start the application:

```bash
docker compose pull
docker compose up -d
```

Check startup and health:

```bash
docker compose ps
docker compose logs --tail=100 tuvima
docker inspect --format '{{.State.Health.Status}}' tuvima-library
```

First startup can remain in `starting` while configuration is seeded and the services initialize. `healthy` means both the internal Engine and Dashboard liveness checks pass.

## Claim the server

Open `http://SERVER-IP:5016/setup`. The first screen asks for a one-time claim token. Read it from container output:

```bash
docker logs tuvima-library 2>&1 | grep "\[Tuvima Setup\] Claim token"
```

Enter the token promptly. It is kept only in Engine memory, is single-use, and is exchanged for a setup session that expires after 12 hours. Complete the administrator step and save the recovery codes outside the server.

If the container restarted before you used the token, read the new token from the latest logs. Never publish container logs or paste the token into an issue.

## Verify the supported boundary

- Open only `http://SERVER-IP:5016` on the LAN.
- Do not publish port `61495`.
- Confirm `/config`, `/db`, `/backups`, and `/models` are bind-mounted before importing media.
- Create the first recovery point in **Settings → Backup & Recovery**, download it, and run **Test restore**.
- Use [Operations and Recovery](../guides/operations-and-recovery.md) before upgrading or enabling remote access.

For Docker installation details, see the official [Docker Compose installation guide](https://docs.docker.com/compose/install/).
