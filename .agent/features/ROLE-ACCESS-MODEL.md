# Feature: Role Access Model

> **Mirrors:** `CLAUDE.md` §3.3 (Security) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-07-16 | Auditor: Codex

---

## The three roles

Tuvima Library defines three user roles with progressively wider control:

| Role | Purpose | Can personalise? | Can curate? | Can administer? |
|---|---|---|---|---|
| **Consumer** (Viewer) | Browse and enjoy the library | Yes | No | No |
| **Curator** | Fix metadata and confirm uncertain items | Yes | Yes | Limited |
| **Administrator** | Full local configuration and access control | Yes | Yes | Yes |

---

## User experience

### What a Consumer sees

- The full library and profile-owned My List.
- User Settings.
- Profile switching, Settings, and Help in the account menu.
- No Needs Review row, notification count, or review-count request.

### What a Curator sees

- Everything a Consumer sees.
- Needs Review and its count in the account menu.
- The Settings/Admin destinations made visible by `SettingsNav`.

### What an Administrator sees

- Everything a Curator sees.
- All Settings/Admin destinations, including libraries, providers, users, access, diagnostics, ingestion, and Local AI.

Sign out is independent of role: it is shown only when OIDC or hybrid authentication is enabled. Local-only users switch profiles instead.

---

## Business rules

| # | Rule | Where enforced |
|---|---|---|
| RAR-01 | Consumer profiles do not request or render Needs Review. | Dashboard (`SettingsNav.IsVisible`, `TopNavAccountMenu`, `UIOrchestratorService`) |
| RAR-02 | Settings navigation hides admin-only destinations from Consumer profiles. | Dashboard (`SettingsNav`) |
| RAR-03 | The seed Owner profile cannot be deleted. | Engine (`ProfileService`) |
| RAR-04 | The last Administrator cannot be deleted. | Engine (`ProfileService`) |
| RAR-05 | Role assignment is managed through Users & Access. | Dashboard and Engine profile endpoints |
| RAR-06 | UI visibility is not authorization; protected Engine endpoints still declare a role guard. | Engine endpoint definitions |

---

## Platform health

| Area | Status | Notes |
|---|---|---|
| UI navigation filtering | **PASS** | `SettingsNav` filters destinations from the active browser profile role. |
| Account menu filtering | **PASS** | Needs Review and its count are hidden for Consumer profiles. |
| Active role resolution | **PASS** | `ActiveProfileSessionService` persists and resolves the browser's current profile. |
| Engine-side role enforcement | **GATING REQUIRED** | Every protected endpoint must retain an explicit authorization guard; UI filtering alone is not a security boundary. |
| Profile CRUD | **PASS** | Seed and last-administrator protections remain in the Engine. |

---

## What "GATING REQUIRED" means

The Dashboard's permission-aware menu improves the local experience, but hiding a control does not secure its API. Before exposing Tuvima beyond the trusted local environment, verify API-key role association, endpoint guards, and the local-bypass policy for every administrative route.

---

## Product owner summary

The active local profile now drives both Settings navigation and the account menu. Consumers get a clean personal menu without review alerts, while Curators and Administrators see Needs Review when they are allowed to act on it. Engine authorization remains the security boundary.
