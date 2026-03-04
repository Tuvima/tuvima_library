# Feature: Settings & Preferences

> **Mirrors:** `CLAUDE.md` §3.8 (Activity Ledger), §3.12 (Device Profiles) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-01 | Auditor: Claude (Product-Led Architect)

---

## Architecture Summary

Settings are split into two separate experiences:

| Page | URL | Who it's for | What it controls |
|------|-----|---------------|-----------------|
| **Preferences** | `/preferences` | Every user | Personal look-and-feel (theme, accent colour, display name) |
| **Server Settings** | `/server-settings` | Administrators & Curators | Library folders, metadata sources, API keys, user management |

A backward-compatible redirect exists at `/settings` — old bookmarks land safely on Preferences.

---

## User Experience

### Preferences (any user)

The user opens Preferences from the avatar menu in the top-left corner of the app bar.

**General tab (working)**
- Edit your display name (up to 50 characters).
- Pick an avatar colour from 8 preset swatches. Your initial is generated from the first letter of your name.
- Your role (Administrator, Curator, or Consumer) is displayed but cannot be changed here — only an Administrator can change roles.
- Toggle dark/light mode.
- Pick an accent colour from 8 preset swatches (Violet, Teal, Amber, Pink, Blue, Green, Deep Orange, Purple).
- Press "Save Profile" to persist changes.

**Playback tab (placeholder)**
- Displays a "Feature in Development" message. No functionality yet.

### Server Settings (admin / curator only)

The user opens Server Settings from the avatar menu — but this menu item only appears if the active profile's role is Administrator or Curator. A Consumer never sees this option.

**Libraries tab (working)**
- Set the Watch Folder path (your "inbox" where you drop files).
- Set the Library Folder path (where Tanaste organises files).
- Both paths show a live health dot: green = accessible, red = not accessible.
- "Test Path" buttons probe the folder for existence, read access, and write access.
- Set the Organisation Template — a tokenised pattern that controls how files are named and nested when organised.
- "Preview" shows a sample resolved path so you can see the result before saving.
- Background health checks run every 30 seconds and update the dots in real time.

**Metadata tab (working)**
- Providers are grouped into collapsible categories by media type: Ebooks, Audiobooks, Movies, Universal.
- Each provider appears as an enriched card showing: status badge (Reachable/Unreachable/Disabled), display name, domain chip, zero-key badge, capability icons, trust score bar, per-field trust weights (expandable), and enable/disable toggle.
- Administrators can toggle providers, edit them, or remove them. Curators see a read-only view.
- An "Add Source" wizard (three-step drawer) collects registration details for future custom provider support.
- See `METADATA-MANAGEMENT.md` for full details.

**Connectivity tab (placeholder)**
- "Feature in Development" message. Reserved for future network/sync settings.

**API Keys tab (working)**
- View all issued Guest API Keys (label + masked key + creation date).
- Reveal the first few characters of a key for identification.
- Generate a new key with a label — the full key is shown exactly once (copy-to-clipboard).
- Revoke individual keys.
- "Revoke All" with a confirmation dialog.

**Users tab (working)**
- View all profiles (avatar, name, role chip, creation date).
- Create a new profile: choose a name, pick a role (Administrator / Curator / Consumer), and select an avatar colour.
- Delete a profile — but the seed Owner profile and the last remaining Administrator cannot be deleted.

**Maintenance tab (placeholder)**
- "Feature in Development" message. Reserved for future database diagnostics and housekeeping.

**Curator's Drawer (working)**
- A slide-in side panel (480px, right-anchored) for metadata corrections.
- Search for works by title, select a match, and apply it as a "user-locked" correction.
- View the full claim history for any entity.
- Corrections made here override all automated scoring permanently.

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| BR-01 | Only Administrator and Curator roles can see the "Server Settings" menu item. | Dashboard (MainLayout avatar menu) |
| BR-02 | Within Server Settings, admin-only tabs (Libraries, Connectivity, API Keys, Users, Maintenance) are hidden from non-admin roles. | Dashboard (SettingsTabBar role filter) |
| BR-03 | The Metadata tab is visible only to Administrators and Curators (AdminOnly = true). | Dashboard (SettingsTabBar: AdminOnly = true) |
| BR-04 | The seed Owner profile (ID `00000000-...01`) cannot be deleted. | Engine (ProfileService business rule) |
| BR-05 | The last remaining Administrator profile cannot be deleted. | Engine (ProfileService business rule) |
| BR-06 | API key plaintext is shown exactly once at creation time; after that only the label and creation date are retrievable. | Engine (AdminEndpoints) |
| BR-07 | Provider secrets are encrypted at rest using the OS Data Protection API. | Engine (DataProtectionSecretStore) |
| BR-08 | Provider secrets are masked as '********' when read back via the API — the masked sentinel is rejected if sent as a write. | Engine (AdminEndpoints) |
| BR-09 | Organisation templates must pass sample-based validation before being saved. | Engine (FileOrganizer.ValidateTemplate) |
| BR-10 | Folder health is probed every 30 seconds; only state changes trigger a Dashboard update. | Engine (FolderHealthService) |
| BR-11 | User-locked metadata corrections override all automated scoring permanently (confidence = 1.0). | Engine (ScoringEngine) |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| Preferences > General | **PASS** | Fully functional. Minor note: accent swatch highlight only checks dark palette — may not highlight correctly in light mode. |
| Preferences > Playback | **WARN** | Placeholder only. No user impact — clearly labelled as "in development." |
| Server Settings > Libraries | **PASS** | Fully functional with live health monitoring. Note: "Preview" also saves the template (side-effect). |
| Server Settings > Metadata | **PASS** | Fully functional. Two providers (Local Filesystem, Open Library) never show reachability because they have no probe endpoint. |
| Server Settings > Connectivity | **WARN** | Placeholder only. |
| Server Settings > API Keys | **PASS** | Fully functional with one-time key reveal and clipboard support. |
| Server Settings > Users | **PASS** | Fully functional with seed-protection and last-admin guardrails. |
| Server Settings > Maintenance | **WARN** | Placeholder only. |
| Curator's Drawer | **PASS** | Fully functional. Debounced search, claim locking, history panel. |
| Role-based access (UI) | **WARN** | Tab filtering logic exists but the active role is currently hardcoded to "Administrator." No real role enforcement until session management is wired. |
| Role-based access (API) | **GATING REQUIRED** | Engine endpoints have zero role checks. Any caller can hit /admin, /profiles, /settings without being an Administrator. The ApiKeyMiddleware passes all requests that lack a key header. |

---

## PO Summary

Settings and Preferences are split into two clean pages — one for personal look-and-feel, one for server administration. Seven of the ten tabs are fully working (General, Libraries, Metadata, API Keys, Users, Curator's Drawer, and the redirect shim); three are clearly-labelled placeholders (Playback, Connectivity, Maintenance). **The most important gap is security: the Engine does not currently enforce role-based access on its endpoints — any caller can perform admin actions, which must be addressed before external access is enabled.**
