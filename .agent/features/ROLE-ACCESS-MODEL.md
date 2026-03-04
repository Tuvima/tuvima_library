# Feature: Role Access Model

> **Mirrors:** `CLAUDE.md` §3.3 (Security) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-01 | Auditor: Claude (Product-Led Architect)

---

## The three roles

Tanaste defines three user roles, each with a progressively wider scope of control:

| Role | Purpose | Can personalise? | Can curate? | Can administer? |
|------|---------|------------------|-------------|-----------------|
| **Consumer** (Viewer) | Browse and enjoy the library | Yes | No | No |
| **Curator** | Fix metadata, correct misidentified files | Yes | Yes | No |
| **Administrator** | Full control — folders, keys, users, providers | Yes | Yes | Yes |

---

## User Experience

### What a Consumer sees
- The full library (Hubs, Works, Editions).
- The Preferences page (General + Playback tabs).
- The avatar menu shows "Preferences" only — "Server Settings" is hidden.
- No access to the Curator's Drawer.

### What a Curator sees
- Everything a Consumer sees, plus:
- The avatar menu shows both "Preferences" and "Server Settings."
- Inside Server Settings, only the **Metadata** tab is visible (the only tab not marked admin-only).
- Access to the **Curator's Drawer** for locking/correcting metadata values.
- Cannot see Libraries, Connectivity, API Keys, Users, or Maintenance tabs.

### What an Administrator sees
- Everything a Curator sees, plus:
- All six Server Settings tabs are visible.
- Can manage watch/library folders, toggle providers, generate/revoke API keys, create/delete user profiles, and (eventually) run maintenance tasks.

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| RAR-01 | The avatar menu hides "Server Settings" for Consumer-role profiles. | Dashboard (MainLayout: `_isAdminOrCurator` flag) |
| RAR-02 | The SettingsTabBar hides admin-only tabs when the active role is not Administrator or Curator. | Dashboard (SettingsTabBar: `HasAdminAccess` check) |
| RAR-03 | The Metadata tab is the only Server Settings tab accessible to Curators. | Dashboard (SettingsTabBar: `AdminOnly = false` for Metadata) |
| RAR-04 | The seed Owner profile cannot be deleted. | Engine (ProfileService) |
| RAR-05 | The last Administrator cannot be deleted (prevents lockout). | Engine (ProfileService) |
| RAR-06 | Role assignment is done by an Administrator via the Users tab. | Dashboard (UsersTab: role selector in create dialog) |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| UI tab filtering | **PASS** | Logic is correct and tested — SettingsTabBar correctly hides tabs based on role. |
| Avatar menu filtering | **PASS** | MainLayout conditionally renders the "Server Settings" link based on profile role. |
| Active role resolution | **WARN** | Currently hardcoded to "Administrator" in ServerSettings.razor. Real session-based profile selection is not yet wired. All users see all tabs until this is completed. |
| Engine-side role enforcement | **GATING REQUIRED** | **Zero role checks exist on Engine endpoints.** Any HTTP caller — regardless of role — can hit `/admin/api-keys`, `/settings/folders`, `/profiles`, etc. The ApiKeyMiddleware only validates key existence, not the caller's role. This must be addressed before the system is exposed beyond localhost. |
| Profile CRUD | **PASS** | Create, read, update, delete all working with proper guardrails (seed protection, last-admin protection). |

---

## What "GATING REQUIRED" means

The Dashboard hides UI elements based on role — but the Engine does not enforce roles. Today this is acceptable because Tanaste runs on localhost and the only user is the Product Owner. **However, the moment Guest API Keys are issued to external tools (Radarr, Sonarr, a mobile app), those tools will have unrestricted access to every endpoint — including user management and API key revocation.**

Before external access is enabled, the Engine needs:
1. A way to associate an API Key with a role (or inherit the issuing user's role).
2. Middleware that checks the caller's role against the endpoint's required permission level.
3. A decision on whether unauthenticated (no-header) requests should be blocked or treated as the local owner.

---

## PO Summary

The three-tier role model (Consumer, Curator, Administrator) is fully designed and the Dashboard correctly hides or shows features based on role. **However, the role currently defaults to "Administrator" for everyone (placeholder), and the Engine has no role enforcement at all — any caller can perform any action. This is safe for local-only use today, but must be resolved before issuing API keys to external tools.**
