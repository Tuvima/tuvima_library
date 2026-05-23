---
title: "Product Status"
summary: "A clear Early Access view of what Tuvima Library can do today and what is still planned."
audience: "user"
category: "reference"
product_area: "product"
tags:
  - "status"
  - "early-access"
  - "roadmap"
---

# Product Status

Tuvima Library is in **Early Access**. The core architecture is real, the Engine and Dashboard run locally, and many workflows are usable today. Some areas are still partial, intentionally read-only, or planned for later phases. This page keeps that distinction clear.

For the row-by-row implementation truth table, see the [Feature Truth Inventory](feature-truth-inventory.md).

## Built Today

| Area | Status | What this means |
|---|---|---|
| Engine and Dashboard | Live | The API host and Blazor Dashboard run locally and communicate over HTTP and SignalR. |
| Home, Read, Watch, Listen | Partial | Browse surfaces render real Engine display data when available. Quality depends on ingested library data. |
| Collections | Partial | Broader rollups, playlists, and managed collections exist where backed by Engine data. Lane-level shelves stay in their media lanes. |
| Search | Partial | Cross-library search is wired to Engine display/search APIs. Results depend on indexed metadata. |
| Detail pages | Live | Item detail surfaces show metadata and launch inline editing through the shared editor. |
| Review Queue | Live | Uncertain or blocked items can be reviewed, dismissed, skipped for universe/QID, or resolved through Engine-backed actions. |
| Ingestion dashboard | Partial | Active operations, recent batches, folder health, provider health, progress, and review reasons are visible where Engine data exists. |
| Settings > Libraries | Live | Folder paths, organization templates, path checks, save, and scan actions are backed by Engine/config APIs. |
| Settings > Providers | Live | Provider catalogue/status/config, credential state, health, tests, and pipeline priority are backed where the Engine exposes them. |
| Settings > Local AI | Live | Model inventory, download/cancel/load/unload, hardware profile, benchmark, resources, feature flags, vocabulary, and schedules are connected where endpoints exist. |
| Playback and reader preferences | Live | Personal playback, reading, subtitle, resume, and progress preferences persist through the playback settings API. |
| Plugins | Partial | Plugin list, enable/disable, settings JSON, dynamic manifests, health, jobs, and approved-catalog lookup are available. |
| Users and access | Partial | Profiles and API keys are Engine-backed. Remote access and some network controls are still read-only or not connected. |

## Still Outstanding

These items are not presented as complete user workflows yet:

- A full first-run wizard beyond the current Setup checklist.
- Advanced direct-play, delivery, subtitle/audio policy, and automated offline-download controls.
- Plugin marketplace install/update flows.
- Some Local AI job controls, deletion actions, and per-feature runtime integrations.
- Richer playlist editing, recommendation automation, smart collections, and broader discovery intelligence.
- Full remote-access hardening and account/session policy beyond the local-first role/API-key model.
- Interoperability targets such as OPDS, Audiobookshelf-compatible APIs, import wizards, webhooks, and PWA behavior.

## Product Guardrails

- Normal media corrections happen inline from the page, card, row, album, track, movie, show, book, comic, or detail view where the issue appears.
- Review Queue is only for blocked, uncertain, low-confidence, or unresolved items that need human confirmation.
- Settings/Admin is for configuration and operational state.
- The retired all-in-one correction workspace must not return as a current product surface.
- Future-state documents must say they are future-state documents.

## Related

- [Getting Started](../tutorials/getting-started.md)
- [How File Ingestion Works](../explanation/how-ingestion-works.md)
- [Privacy and Local-First Behavior](../explanation/privacy-local-first.md)
- [Target State](../architecture/target-state.md)
