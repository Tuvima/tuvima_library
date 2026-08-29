---
title: "Security Architecture"
summary: "Deep technical documentation for authentication, authorization, rate limiting, and secure service boundaries."
audience: "developer"
category: "architecture"
product_area: "security"
tags:
  - "security"
  - "api-keys"
  - "authorization"
---

# Security Architecture

## Secret Store

Private API keys for external metadata providers such as TMDB, Comic Vine, Fanart.tv, and OpenSubtitles are config-file secrets. Base provider definitions live under `config/providers/*.json`; long-lived local credentials belong in matching gitignored overlays under `config/secrets/{provider}.json`. A blank API key in a base provider file does not mean the effective runtime key is missing if the matching secrets file exists.

## Guest Key System

Any application that wants to communicate with the Engine must present a valid API key. Keys are:

- Generated inside the Engine with an assigned role (Administrator, Curator, or Consumer)
- Given a human-readable label (e.g. "Media manager integration", "Mobile app")
- Revocable individually without affecting any other active keys

## Mandatory Authentication

Every Engine endpoint requires authentication, with two exceptions:

- `/system/status` - the health probe endpoint, always open without authentication
- Localhost requests - when `MediaEngine:Security:LocalhostBypass` is `true` (the default), requests originating from the local machine are treated as Administrator without requiring a key. This preserves the local development and home-server experience.

All other unauthenticated requests receive `401 Unauthorized`.

## Local Administrator Password Recovery

Tuvima Library does not expose an anonymous password-reset endpoint, including
on localhost. A loopback request proves where a request originated, but it does
not prove that the person making it owns or administers the library.

Local administrator recovery has two supported paths:

- Use one of the one-time recovery codes generated during first-run setup. A
  successful recovery rotates the full set, so the used and previously saved
  codes can no longer be reused.
- Run the bundled `tuvima-admin auth reset-password` command on the Engine host.
  The command requires an elevated Windows administrator terminal or effective
  user ID 0 on Linux, macOS, and containers. It reads the new password from an
  interactive, non-echoing prompt and refuses redirected input, so passwords
  are never accepted as command-line arguments or shell input.

For a source checkout, run this from an elevated terminal at the repository root:

```powershell
dotnet run --project src/MediaEngine.Admin -- auth reset-password --config-dir config
```

For the official container image, run:

```bash
docker exec -it --user 0 tuvima-library /app/admin/tuvima-admin auth reset-password
```

The host command verifies that the named credential belongs to an Administrator,
changes its security stamp, clears password lockout state, revokes every active
session for that profile, rotates all recovery codes, and records a security
audit event. It fails if it cannot locate an existing library database and never
creates or migrates a database as part of recovery.

This design follows the same physical-host proof used by established local
servers: Jellyfin writes a short-lived reset artifact into its server data
directory, while Immich and Grafana provide server-side administrator commands.
Tuvima uses the command model so resetting access requires operating-system or
container administration rather than mere access to the Dashboard URL.

## Role-Based Authorization

Each API key carries one of three roles:

**Administrator** - Full access to all endpoints.

**Curator** - Can browse the library, stream files, read and write metadata claims, and view provider status. Cannot access admin operations, folder settings, ingestion controls, or profile management.

**Consumer** - Can browse the library, stream files, and read metadata claim history. Cannot modify metadata or access any settings endpoints.

## Endpoint Role Guards

Authentication (via `ApiKeyMiddleware`) only proves a caller has a valid key; it does not by itself
restrict *which* endpoints that key can reach. Role restriction is applied per endpoint through
`RoleAuthorizationFilter` (`src/MediaEngine.Api/Security/RoleAuthorizationFilter.cs`) using three
fluent extensions — `RequireAdmin()`, `RequireAdminOrCurator()`, `RequireAnyRole()` — with overloads
for both an individual route (`RouteHandlerBuilder`) and a whole `MapGroup(...)` (`RouteGroupBuilder`).

Guards are applied at whichever level matches the endpoint's shape:

- **Group-level** — when every route under a group shares the same minimum role (for example, all of
  `/persons`, `/library` (characters), `/timeline`, `/progress`, and the universe graph routes require
  any authenticated role), the guard is chained directly onto the `MapGroup(...)` declaration so every
  route mapped on that group inherits it.
- **Per-route escalation** — a route that needs a stricter guard than its group can still add its own
  `Require*()` call on top; both filters run, so the stricter one wins. For example, the universe graph
  group requires any role, but `POST /universe/entity/{qid}/deep-enrich` additionally requires
  `RequireAdminOrCurator()`.
- **Per-route only** — groups with mixed read/write access levels (for example `/collections`,
  `/metadata`, `/settings`) keep guards on individual routes rather than the group.

Every `Require*()` call — group or route level — also attaches `RoleRequirementMetadata` (an
endpoint-metadata record listing the allowed roles) via `WithMetadata(...)`. This makes the role
requirement discoverable from endpoint metadata rather than only from the filter pipeline, which is
what `RouteAuthorizationGuardrailTests` (`tests/MediaEngine.Api.Tests/RouteAuthorizationGuardrailTests.cs`)
scans for: every mapped route in `src/MediaEngine.Api/Endpoints/*.cs` must resolve a `Require*()` guard,
either on its own chain or on its file's `MapGroup(...)` declaration, with a narrow allowlist for
`/system/status` (the intentionally open connectivity probe), `/health`, and Swagger.

Surfaces brought under a guard as part of the security hardening pass: the universe graph endpoints
(`/universes`, `/universe/{qid}/...`), people (`/persons/...`), library characters
(`/library/characters/...`, `/library/portraits/...`, `/library/persons/{id}/character-roles`,
`/library/universes/{qid}/characters`, `/library/assets/{id}`, `/library/enrichment/universe/trigger`),
the entity timeline (`/timeline/...`), playback/reading progress (`/progress/...`), the provider
catalogue (`/providers/catalogue`), canon discrepancy detection (`/metadata/{id}/canon-discrepancies`),
pipeline settings (`/settings/pipelines`) and provider icons (`/settings/providers/{name}/icon`), UI
library preferences (`/settings/ui/library-preferences`), metadata search-cache
(`/metadata/{id}/search-cache`) and label resolution (`/metadata/labels/resolve`), and the collection
series manifest (`/collections/{id}/series-manifest`).

## API Key Lookup Cache

Every authenticated request hashes the incoming `X-Api-Key` header and looks it up. Rather than hitting the database on every single request, `ApiKeyMiddleware` calls `IApiKeyLookupCache` — a private, service-owned, in-memory cache that sits in front of `IApiKeyRepository`:

- Both matches and "not found" results are cached, so a flood of invalid-key guesses cannot force a database round trip per request.
- Entries expire after 30 seconds (absolute TTL), which caps the maximum time it could take for a change to be noticed if nothing invalidated the cache proactively.
- Creating or revoking a key (`POST /admin/api-keys`, `DELETE /admin/api-keys/{id}`, `DELETE /admin/api-keys`) clears the cache immediately, so in the normal case a revoked key stops working right away rather than waiting out the TTL. The 30-second TTL is only the worst-case ceiling.
- The cache is capped at 1024 entries so it cannot grow without bound under a scanning/brute-force attempt.

## Rate Limiting

Three rate-limiting policies protect the Engine from abuse or runaway automation:

| Policy | Limit |
|--------|-------|
| Key generation | 5 requests / minute / IP |
| File streaming | 100 requests / minute / IP |
| General API | 60 requests / minute / IP |

The general policy is also registered as the Engine's process-wide default (`GlobalLimiter`), so every
connection point is throttled per IP even if it never opts into a named policy explicitly. Rate limiting
runs before API key authentication in the request pipeline, so a flood of unauthenticated requests is
throttled before it can trigger a database lookup.

A small set of paths are exempt from the general default because they either must always stay reachable
or already carry their own, differently-tuned policy that would otherwise be double-throttled underneath it:

- `/system/status`, `/health` — health/status probes that must remain reachable for monitoring
- `/swagger` — API documentation, development-only
- the SignalR Intercom hub path — real-time push connections, not request-driven traffic
- `/stream`, `/read`, `/playback` — carry the higher-limit streaming policy for media playback
- `/admin/api-keys` — carries the stricter key-generation policy

## Path Traversal Protection

Folder-related endpoints (`PUT /settings/libraries`,
`PUT /settings/incoming-sources`, and `/settings/test-path`) reject paths that
contain `..` traversal segments or target known system directories (for
example, `C:\Windows` or `/etc`). This prevents an authorized client from
accidentally or maliciously navigating outside the intended library roots.

## SignalR Collection Authentication

The real-time Intercom at `/intercom` requires authentication via one of:

- `X-Api-Key` request header
- `access_token` query string parameter
- Localhost bypass (when `LocalhostBypass` is enabled)

Unauthenticated connection attempts from non-localhost origins are rejected before the WebSocket handshake completes.

## Related

- [Engine API Reference](../reference/api-endpoints.md)
- [How to Build, Test, and Verify Changes](../guides/running-tests.md)
- [Settings Architecture and Review Queue](dashboard-ui.md)
