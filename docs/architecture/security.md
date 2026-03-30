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

Private API keys (for external metadata providers such as TMDB or MusicBrainz) are encrypted at rest using the operating system's built-in protection layer. They are never stored as plain text in any configuration file.

## Guest Key System

Any application that wants to communicate with the Engine must present a valid API key. Keys are:

- Generated inside the Engine with an assigned role (Administrator, Curator, or Consumer)
- Given a human-readable label (e.g. "Media manager integration", "Mobile app")
- Revocable individually without affecting any other active keys

## Mandatory Authentication

Every Engine endpoint requires authentication, with two exceptions:

- `/system/status` â€” the health probe endpoint, always open without authentication
- Localhost requests â€” when `MediaEngine:Security:LocalhostBypass` is `true` (the default), requests originating from the local machine are treated as Administrator without requiring a key. This preserves the local development and home-server experience.

All other unauthenticated requests receive `401 Unauthorized`.

## Role-Based Authorization

Each API key carries one of three roles:

**Administrator** â€” Full access to all endpoints.

**Curator** â€” Can browse the library, stream files, read and write metadata claims, and view provider status. Cannot access admin operations, folder settings, ingestion controls, or profile management.

**Consumer** â€” Can browse the library, stream files, and read metadata claim history. Cannot modify metadata or access any settings endpoints.

## Rate Limiting

Three rate-limiting policies protect the Engine from abuse or runaway automation:

| Policy | Limit |
|--------|-------|
| Key generation | 5 requests / minute / IP |
| File streaming | 100 requests / minute / IP |
| General API | 60 requests / minute / IP |

## Path Traversal Protection

Folder-related endpoints (`/settings/folders`, `/settings/test-path`) reject any path that contains `..` traversal segments or targets known system directories (e.g. `C:\Windows`, `/etc`). This prevents an authorized client from accidentally or maliciously navigating outside the intended library roots.

## SignalR Hub Authentication

The real-time Intercom at `/hubs/intercom` requires authentication via one of:

- `X-Api-Key` request header
- `access_token` query string parameter
- Localhost bypass (when `LocalhostBypass` is enabled)

Unauthenticated connection attempts from non-localhost origins are rejected before the WebSocket handshake completes.

## Related

- [Engine API Reference](../reference/api-endpoints.md)
- [How to Build, Test, and Verify Changes](../guides/running-tests.md)
- [Settings Architecture and Library Vault](settings-and-vault.md)
