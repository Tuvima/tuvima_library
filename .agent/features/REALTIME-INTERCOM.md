# Feature: Real-Time Intercom

> **Mirrors:** `CLAUDE.md` §3.4 (Dashboard UI — Real-time updates) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-01 | Auditor: Claude (Product-Led Architect)

---

## User Experience

The Dashboard maintains a persistent, live connection to the Engine. When something happens — a new file is ingested, metadata arrives from an external source, a person's portrait is fetched — the Dashboard updates instantly. No page refresh, no polling, no waiting.

### What the user sees

- **New Hub tiles appear** in the grid as files are ingested.
- **Metadata enriches** — cover art, descriptions, and author details pop in moments after the initial scan.
- **Folder health dots** on the Libraries tab turn green or red as folder accessibility changes.
- If the connection drops, it **reconnects automatically** (0s → 2s → 10s → 30s backoff).

---

## Events in the system

| Event | When it fires | What the Dashboard does |
|-------|---------------|------------------------|
| **MediaAdded** | A new Work is committed to the library. | Invalidates the Hub cache; grid refreshes. |
| **IngestionProgress** | Active ingestion tick (per-file). | Updates the progress state (not yet surfaced in UI). |
| **MetadataHarvested** | An external provider returned better metadata. | Invalidates the Hub cache; tile details update. |
| **PersonEnriched** | Wikidata returned an author/narrator portrait. | Buffers the update (up to 50 items). |
| **WatchFolderActive** | The Watch Folder path was changed. | Updates the state container. |
| **FolderHealthChanged** | 30-second health probe detected a change. | Updates the folder health dots on the Libraries tab. |

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| RIR-01 | SignalR failure never aborts the ingestion pipeline — events are published with "safe publish" wrappers. | IngestionEngine (SafePublishAsync) |
| RIR-02 | Reconnection uses automatic backoff: 0s, 2s, 10s, 30s. | UIOrchestratorService (HubConnectionBuilder) |
| RIR-03 | The activity log is capped at 100 entries. | UniverseStateContainer |
| RIR-04 | Person update buffer is capped at 50 entries. | UniverseStateContainer |
| RIR-05 | Folder health changes are broadcast only when the state actually changes (not every 30 seconds). | FolderHealthService (change-detection pattern) |
| RIR-06 | All events are broadcast to all connected clients — no per-client filtering. | SignalREventPublisher (Clients.All) |

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| SignalR connection | **PASS** | Reliable connection with automatic reconnect and backoff. |
| MediaAdded event | **PASS** | Fires correctly, invalidates cache, grid refreshes. |
| MetadataHarvested event | **PASS** | Fires correctly with updated canonical values. |
| FolderHealthChanged event | **PASS** | Change-detection pattern prevents unnecessary broadcasts. |
| WatchFolderActive event | **PASS** | Fires on folder settings save. |
| PersonEnriched event | **WARN** | Fires but has an empty person name due to known bug (passes `Guid.Empty` to asset lookup). |
| IngestionProgress event | **WARN** | Fires correctly but is not yet surfaced in the Dashboard UI (no progress bar on the Home page). |
| Event authentication | **PASS** | Connections require a valid API key (header or query string) or localhost bypass. Enforced by `IntercomAuthFilter`. |
| Event scoping | **WARN** | All clients receive all events. No per-role or per-session filtering. |

---

## PO Summary

The real-time update system works reliably — new files, enriched metadata, and folder health changes all push to the Dashboard instantly with automatic reconnection on failure. The SignalR hub now requires authentication (Phase A Security Foundation). **One remaining concern: the PersonEnriched event sends an empty name (cosmetic bug).**
