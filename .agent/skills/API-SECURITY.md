# Skill: API Security & Authentication

> **Mirrors:** `CLAUDE.md` §3.3 (Security) — keep both in sync per `.agent/SYNC-MAP.md`

> Last updated: 2026-03-01

---

## Purpose

This skill covers the Engine's security model — API key middleware, secret management, CORS, and SignalR authentication.

---

## Key files

| File | Role |
|------|------|
| `src/MediaEngine.Api/Middleware/ApiKeyMiddleware.cs` | Request authentication (validate-if-present model) |
| `src/MediaEngine.Api/Services/ApiKeyService.cs` | Key generation (256-bit random) and SHA-256 hashing |
| `src/MediaEngine.Api/Services/DataProtectionSecretStore.cs` | Provider secret encryption at rest |
| `src/MediaEngine.Api/Services/FolderHealthService.cs` | Background health probe (broadcasts paths via SignalR) |
| `src/MediaEngine.Api/Hubs/CommunicationHub.cs` | SignalR hub (server-to-client only) |
| `src/MediaEngine.Api/Services/SignalREventPublisher.cs` | Broadcasts events to all connected clients |
| `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs` | API key CRUD + provider config management |
| `src/MediaEngine.Storage/ApiKeyRepository.cs` | Key hash storage (plaintext never stored) |
| `src/MediaEngine.Storage/ProviderConfigurationRepository.cs` | Encrypted secret storage |

---

## Current authentication model

```
Request arrives
  ├── X-Api-Key header absent → PASSES THROUGH (no identity)
  ├── X-Api-Key header present + empty → 401
  ├── X-Api-Key header present + invalid hash → 401
  └── X-Api-Key header present + valid hash → PASSES THROUGH (with ApiKeyId/Label in HttpContext.Items)
```

**There is no mandatory authentication and no role-based authorization on any endpoint.**

---

## Middleware pipeline order

```
1. CORS ("BlazorWasm" policy — localhost ports only)
2. ApiKeyMiddleware (validate-if-present)
3. [Dev only] Swagger UI
4. Endpoint routing (all Map*Endpoints calls)
```

**Missing:** `UseAuthentication()`, `UseAuthorization()`, `UseRateLimiter()`, `UseHttpsRedirection()`.

---

## To implement mandatory authentication

1. Change `ApiKeyMiddleware` to reject requests without `X-Api-Key` header (return 401).
2. Exempt specific routes that must remain public: `GET /system/status`, Swagger (dev only).
3. Add the `X-Api-Key` header as a security scheme in the Swagger definition so it can be tested.
4. Decide whether the Dashboard (same machine) should use a key or be exempted by origin.

## To implement role-based authorization

1. Associate each API key with a `ProfileRole` (Administrator, Curator, Consumer) at creation time.
2. In the middleware, after validating the key, load the associated role and set it as a claim.
3. Add `[RequireRole(...)]` or equivalent endpoint filters to admin, settings, and metadata endpoints.
4. Decide whether unauthenticated (no-header) requests are treated as the local Owner or blocked.

## To implement rate limiting

1. Register `AddRateLimiter()` in `Program.cs`.
2. Apply policies to sensitive endpoints: key generation, streaming, ingestion triggers.
3. Consider per-key rate limits for external integrations.

---

## Known gaps

1. **No mandatory authentication** — all endpoints accessible without a key.
2. **No role-based authorization** — roles exist but are never checked.
3. **No rate limiting** — no throttling on any endpoint.
4. **Arbitrary path traversal** — `/settings/test-path` and `/settings/folders` accept any filesystem path.
5. **SignalR hub is unauthenticated** — any client can connect and receive all events.
6. **No HTTPS enforcement** — keys could be transmitted in cleartext.
7. **No key expiration** — keys are valid forever.
8. **Unsalted key hashes** — SHA-256 without salt (acceptable for 256-bit random keys).
9. **Broadcast events are unscoped** — all clients receive all events regardless of role.
