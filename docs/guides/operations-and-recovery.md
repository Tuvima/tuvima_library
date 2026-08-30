---
title: "Operations and Recovery"
summary: "Claim, back up, restore, upgrade, roll back, and securely expose a Tuvima installation."
audience: "administrator"
category: "guide"
product_area: "operations"
---

# Operations and Recovery

Use this runbook after installing Tuvima with Docker Compose or a NAS container manager. Commands assume the container name `tuvima-library`; use the equivalent log, restart, and image controls in your NAS UI.

## One-time server claim

An unconfigured Engine writes a one-time claim token to container stdout only:

```bash
docker logs tuvima-library 2>&1 | grep "\[Tuvima Setup\] Claim token"
```

Open `/setup` on the Dashboard, enter the latest token, and complete setup. The token is single-use and never appears in an HTTP response or structured log. Claiming creates a setup session that expires after 12 hours. A restart before claim creates a new token.

The first administrator receives one-time recovery codes. Store them in a password manager or offline recovery record. If all codes are lost, an operator with host control can run the interactive recovery command:

```bash
docker exec -it --user 0 tuvima-library /app/admin/tuvima-admin auth reset-password
```

The command revokes that administrator's sessions and rotates recovery codes. It never accepts the password as a command-line argument.

## Back up and test recovery

Open **Settings → Backup & Recovery** and create a recovery point. Tuvima archives a consistent SQLite snapshot, a manifest, and non-secret configuration. Provider credentials, data-protection secrets, model files, artwork cache, transcodes, and original media are not in the archive.

For the first backup and after material configuration changes:

1. Create the recovery point.
2. Download a copy to another device or backup system.
3. Choose **Test restore** for that recovery point.
4. Confirm the result says the database and configuration archive validated.

A test restore extracts into temporary backup storage, verifies the SQLite database, checks archive boundaries, and removes the temporary files. It does not schedule or apply a restore.

To restore, choose **Restore**, confirm the recovery point, and restart the Engine/container. Tuvima validates and stages the archive while running, applies it before the data store opens on restart, and keeps pre-restore database/configuration copies beside the replaced files.

## Upgrade

Before every upgrade:

1. Read the release notes.
2. Create, download, and test a recovery point.
3. Record the currently running image digest:

```bash
docker inspect --format '{{index .RepoDigests 0}}' tuvima-library
```

4. Pull and recreate the container:

```bash
docker compose pull
docker compose up -d
docker compose ps
```

5. Wait for healthy, then verify **Settings → System Overview**, a representative library page, and **Settings → Ingestion**.

Release tags and `sha-<full-commit>` tags identify immutable builds. The registry also publishes an SBOM and provenance for the multi-platform manifest, and signs its digest with GitHub Actions keyless identity. `latest`, major, and major/minor tags are update channels and can move.

Verify a release with Cosign:

```bash
cosign verify \
  --certificate-identity-regexp='https://github.com/Tuvima/tuvima_library/.github/workflows/docker-publish.yml@refs/(heads/main|tags/v.*)' \
  --certificate-oidc-issuer=https://token.actions.githubusercontent.com \
  ghcr.io/tuvima/tuvima_library@sha256:DIGEST
```

## Roll back

Rollback means selecting the exact previously recorded digest and recreating the container with the same persistent mounts:

```yaml
image: ghcr.io/tuvima/tuvima_library@sha256:RECORDED_DIGEST
```

```bash
docker compose pull
docker compose up -d
```

If the newer version changed disposable pre-beta application state incompatibly, restore the tested pre-upgrade recovery point or clear the disposable application/config state and reingest. Never delete, move, or rewrite user-owned source media as part of rollback.

## TLS reverse proxy

Terminate TLS at a maintained reverse proxy and forward only to Dashboard port `5016`. Never proxy or publish `61495`. Configure the proxy's exact address or isolated container-network CIDR under **Settings → Network & Remote Access → Advanced → Reverse Proxy Trust**, restart the Dashboard, then use **Save and verify** on the Remote Access screen.

Forward WebSocket upgrade headers as well as normal HTTP traffic. Do not trust forwarded headers from every address. Keep the Dashboard sign-in enabled even behind another access layer.

## Tailscale

Tailscale Serve is the recommended private remote path. Use the repository's `deploy/tailscale/docker-compose.tailscale.yml` overlay and keep the auth key in its deployment secret file, outside `/config` and `/backups`.

```bash
docker compose \
  -f docker-compose.yml \
  -f deploy/tailscale/docker-compose.tailscale.yml \
  up -d
```

The sidecar shares Tuvima's network namespace and serves `http://127.0.0.1:5016` to the tailnet with HTTPS. It does not expose the Engine and does not enable Funnel. Confirm the private `https://...ts.net` URL under **Settings → Network & Remote Access → Remote Access** before enabling remote access.

See the official [Tailscale Serve reference](https://tailscale.com/docs/reference/tailscale-cli/serve) for current client behavior.
