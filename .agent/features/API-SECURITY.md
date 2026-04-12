# Feature: API Security & Guest Key System

> **Mirrors:** `CLAUDE.md` §3.3 (Security) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-01 | Phase A Security Foundation applied: 2026-03-01

---

## User Experience

### Guest API Keys

An Administrator can issue "Guest Keys" to allow external tools (Radarr, Sonarr, a mobile app) to talk to the Engine.

1. Open Server Settings -> API Keys tab.
2. Click "Generate Key", enter a label (e.g., "Radarr integration"), and select a role (Administrator, Curator, or Consumer).
3. The full key is displayed exactly once -- copy it now or it's gone forever.
4. The key appears in the table with its label, role, and creation date. The full value is hidden.
5. Individual keys can be revoked, or all keys can be wiped at once (with a confirmation dialog).

### How keys work

When an external tool sends a request with an `X-Api-Key` header, the Engine validates the key's hash against its database. If valid, the request proceeds with the key's assigned role. If invalid, the Engine returns a 401 error. If no key header is sent and the caller is on localhost (with bypass enabled), the request proceeds as Administrator. All other unauthenticated requests are rejected with 401.

### Role-Based Access

Each API key carries one of three roles that determines which endpoints it can access:

| Role | What it can do |
|------|---------------|
| **Administrator** | Full access to everything: admin, settings, library, streaming, ingestion, profiles. |
| **Curator** | Browse library, stream files, read metadata claims, lock/resolve metadata, read provider status. Cannot access admin, folder settings, ingestion, or profile management. |
| **Consumer** | Browse library, stream files, read metadata claim history. Cannot modify metadata, access settings, or perform admin operations. |

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| ASR-01 | API key plaintext is shown exactly once at creation time -- it is never stored or retrievable afterward. | ApiKeyService (SHA-256 hash stored) |
| ASR-02 | API key plaintext is never logged. | ApiKeyMiddleware (explicit code comment) |
| ASR-03 | Provider secrets are encrypted at rest using the OS Data Protection API. | DataProtectionSecretStore |
| ASR-04 | The masked sentinel `********` is rejected if sent as a provider secret value -- prevents accidental mask storage. | AdminEndpoints |
| ASR-05 | The seed Owner profile cannot be deleted. | ProfileService |
| ASR-06 | The last Administrator profile cannot be deleted. | ProfileService |
| ASR-07 | Swagger/OpenAPI is only available in development mode -- not exposed in production. | Program.cs (IsDevelopment gate) |
| ASR-08 | CORS origins are restricted to specific localhost ports. | Program.cs (BlazorWasm policy) |
| ASR-09 | All SQL queries use parameterized statements -- no injection vectors. | All repositories |
| ASR-10 | Every endpoint requires authentication (except `/system/status` and Swagger). | ApiKeyMiddleware (mandatory auth with exempt paths) |
| ASR-11 | Localhost requests bypass authentication when `MediaEngine:Security:LocalhostBypass` is true (default). | ApiKeyMiddleware (loopback detection) |
| ASR-12 | Each API key carries a role (Administrator, Curator, Consumer) checked per-endpoint. | RoleAuthorizationFilter + endpoint `.RequireAdmin()` / `.RequireAdminOrCurator()` / `.RequireAnyRole()` |
| ASR-13 | Folder-related endpoints reject paths containing `..` traversal segments or targeting system directories. | PathValidator (defence-in-depth, applied in SettingsEndpoints) |
| ASR-14 | SignalR collection connections require authentication via `X-Api-Key` header, `access_token` query string, or localhost bypass. | IntercomAuthFilter (IHubFilter) |
| ASR-15 | Key generation is rate-limited to 5 requests/minute per IP. | Program.cs rate limiter ("key_generation" policy) |
| ASR-16 | File streaming is rate-limited to 100 requests/minute per IP. | Program.cs rate limiter ("streaming" policy) |
| ASR-17 | General API calls are rate-limited to 60 requests/minute per IP. | Program.cs rate limiter ("general" policy) |
| ASR-18 | API key role must be one of Administrator, Curator, or Consumer -- validated at generation time. | ApiKeyService (ValidRoles check) |
| ASR-19 | Existing API keys default to Administrator role after migration M-006. | DatabaseConnection (M-006 DEFAULT clause) |

---

## Access Control Matrix

| Endpoint group | Administrator | Curator | Consumer |
|---|---|---|---|
| `/system/status` | *(exempt -- no key needed)* | *(exempt)* | *(exempt)* |
| `/admin/*` (API keys, provider configs) | Full access | Blocked (403) | Blocked (403) |
| `/settings/folders`, `/settings/test-path`, `/settings/organization-template` | Full access | Blocked (403) | Blocked (403) |
| `/settings/providers` (read) | Full access | Full access | Blocked (403) |
| `/settings/providers/{name}` (write) | Full access | Blocked (403) | Blocked (403) |
| `/collections/*` (library listing, search) | Full access | Full access | Full access |
| `/stream/*` (file streaming) | Full access | Full access | Full access |
| `/metadata/claims/*` (read history) | Full access | Full access | Full access |
| `/metadata/lock-claim`, `/metadata/resolve` | Full access | Full access | Blocked (403) |
| `/profiles/*` (list, create, update, delete) | Full access | Blocked (403) | Blocked (403) |
| `/ingestion/*` (scan, library scan) | Full access | Blocked (403) | Blocked (403) |
| `/intercom` (SignalR) | Full access | Full access | Full access |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| API key generation | **PASS** | Cryptographically secure 256-bit random keys, one-time plaintext display, role assignment. |
| API key validation | **PASS** | SHA-256 hash lookup, proper 401 on invalid keys, role propagated to context. |
| API key management UI | **PASS** | Generate, reveal prefix, copy, revoke individual, revoke all -- all working. |
| Mandatory authentication | **PASS** | Every endpoint requires auth; exempt paths limited to `/system/status` and `/swagger`. |
| Role-based authorization | **PASS** | All endpoints enforce role checks via `RoleAuthorizationFilter`. |
| Path traversal protection | **PASS** | `PathValidator` rejects `..` segments and system directories on folder endpoints. |
| SignalR authentication | **PASS** | `IntercomAuthFilter` validates connections via header, query string, or localhost bypass. |
| Rate limiting | **PASS** | Three policies: key generation (5/min), streaming (100/min), general (60/min). |
| Secret encryption at rest | **PASS** | Data Protection API with purpose isolation. |
| Secret masking in API responses | **PASS** | Consistent `********` masking. |
| SQL injection protection | **PASS** | All queries parameterized. |
| Localhost bypass | **PASS** | Configurable via `MediaEngine:Security:LocalhostBypass` (default: true). |
| HTTPS enforcement | **WARN** | No `UseHttpsRedirection()` in the pipeline. API keys sent over plain HTTP would be in cleartext. |
| Key expiration | **WARN** | Keys are valid forever. No expiration, rotation, or last-used tracking. |
| Unsalted key hashes | **WARN** | SHA-256 without per-key salt. Acceptable for 256-bit random keys but deviates from best practice. |

---

## PO Summary

The Engine now enforces a complete security boundary. Every endpoint requires authentication -- either a valid API key or a localhost connection (configurable). Each API key carries a role (Administrator, Curator, Consumer) that determines exactly which endpoints it can access. Folder-related endpoints reject path traversal attacks. The SignalR collection requires authentication. Rate limiting caps key generation (5/min), streaming (100/min), and general API calls (60/min) per IP. **Three remaining items for future improvement: HTTPS enforcement, key expiration/rotation, and salted key hashes.**
