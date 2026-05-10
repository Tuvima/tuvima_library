# Feature Truth Inventory

Phase 0 records what visible product areas can honestly do today. The goal is to keep the current Dashboard model stable: Home, Read, Watch, Listen, Search, detail pages, Review Queue, and Settings/Admin. Deprecated Vault workflows remain removed and must not return as active product behavior.

Status labels:

- Live: implemented, persisted, and user-visible.
- Partial: usable but incomplete or dependent on Engine availability.
- Placeholder: visible as a future destination, but not functionally wired.
- Simulated: local or fake behavior only; should be disabled, relabeled, or removed.
- Experimental: intentionally available, but not guaranteed as product behavior.
- Hidden/removed: should not appear as an active product surface.

| Area | Current status | What works | What does not work | Settings persist | Engine/API support | Visible now | Recommended next phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Home / discovery | Partial | Renders Engine display data when available and shows setup-oriented empty states. | Discovery quality depends on available library data and Engine availability. | Not a settings surface. | Yes, through display APIs. | Yes. | Stabilize during Phase 1/2 as real libraries are configured and ingested. |
| Read lane | Partial | Browse lane shell and reading surfaces render with Engine display data. | Completeness depends on ingestion and metadata quality. | Playback/reading preferences persist through the playback settings API. | Yes, partial display and playback APIs. | Yes. | Improve data completeness after Phase 2 ingestion work. |
| Watch lane | Partial | Browse and player routes exist and use current display/playback contracts. | Advanced playback and delivery controls are not complete. | Personal playback preferences persist; delivery settings do not. | Partial. | Yes. | Delivery and playback expansion belongs after Phase 0. |
| Listen lane | Partial | Music browse shell, quick access, playlists, and playback host render. | Advanced playlist editing and playback behavior remain limited. | Personal playback preferences persist. | Partial. | Yes. | Expand only after product truth baseline is stable. |
| Search | Partial | Cross-library search UI and result rows render. | Result quality depends on indexed library data and Engine availability. | Not a settings surface. | Yes, through search/display contracts. | Yes. | Improve with ingestion/search quality later. |
| Detail pages | Partial | Detail pages render item data and launch the shared editor where available. | Full inline metadata editing is not complete everywhere. | Edits persist only where the shared editor/API path is wired. | Partial. | Yes. | Continue inline editing work without creating a management workbench. |
| Review Queue | Live | Shows uncertain or blocked items and uses the shared review editor flow. | Depends on Engine review data. | Review actions persist through existing review APIs. | Yes. | Yes. | Keep as the exception workflow. |
| Library Operations | Partial | Shows ingestion operations snapshots and progress when Engine data is available. | Historical depth and action coverage are limited. | Operational actions persist only where API-backed. | Yes, partial. | Yes. | Phase 2 ingestion improvements. |
| Settings > Setup | Partial | Readiness checks use Engine status, folders, providers, AI profile/resource status, ingestion, and review count when available. | Phase 1 first-run wizard is not implemented. No fake provider or AI readiness should be shown. | No new setup wizard state. | Yes, read-only readiness plus scan trigger. | Yes. | Phase 1 setup checklist/wizard. |
| Settings > Libraries | Partial | Folder settings and path checks are present. | First-run setup flow is not complete. | Yes where current folder APIs are wired. | Yes. | Yes. | Phase 1 setup and Phase 2 ingestion. |
| Settings > Providers | Partial | Engine-backed provider status, credentials, and pipeline priority can load/save when APIs are available. | Hardcoded defaults are sample-only and must not appear as configured Engine state when loading fails. | Yes for provider config and pipelines when Engine is reachable. | Yes, partial. | Yes. | Provider redesign is out of Phase 0. |
| Settings > Metadata | Partial | Matching and metadata settings surfaces exist. | Some advanced identity/universe behavior remains incomplete. | Mixed JSON/API-backed behavior. | Partial. | Yes. | Later metadata hardening. |
| Settings > Local AI | Partial | Hardware profile and resource status can be read from the Engine. Feature/model/runtime controls are labelled not connected where they are not persisted. | Real model download/load/unload/delete and runtime limit persistence are not implemented. | No for model actions, feature toggles, and runtime limits. | Partial read-only support. | Yes, with truth labels. | Future AI management phase, not Phase 0. |
| Settings > Delivery | Partial | Transcoding/offline tabs may use existing backed components. | Direct Play and subtitle/audio delivery controls are not persisted. | No for Direct Play and subtitle/audio delivery controls. | Partial. | Yes, labelled not connected. | Later delivery/playback work. |
| Settings > Access | Partial | User/access admin surface exists. | Remote access and full security management are not complete. | Partial. | Partial. | Yes, labelled partial in navigation. | Later access/security work. |
| Settings > Plugins | Placeholder | Destination exists for planned extension settings. | Plugin management is not a live product capability. | No complete product persistence path. | Partial or none depending on subsection. | Yes, labelled planned. | Future plugin phase. |
| Playback preferences | Live | Personal playback, reading, subtitle, resume, and progress preferences save through the playback settings API. | Delivery-specific settings are separate and not persisted. | Yes. | Yes. | Yes. | Maintain while expanding playback later. |
| AI model management | Simulated | Hardware/resource readouts can be real. Model list is currently catalogue-only. | Download, load, unload, and delete actions are not connected and must stay disabled. | No. | No action APIs wired in the Dashboard. | Yes, labelled partial/not connected. | Future AI management phase. |
| AI feature toggles | Simulated | Feature descriptions communicate intended AI areas. | Toggles do not persist and do not affect Engine behavior. | No. | No save path. | Yes, disabled and labelled not connected. | Future AI configuration phase. |
| Provider priority | Partial | Engine pipeline configuration can load/save when available. | Hardcoded chains are samples only and should not be treated as current configuration. | Yes when Engine APIs are reachable. | Yes, partial. | Yes. | Later provider management refinement. |
| Direct Play settings | Placeholder | Intended controls are visible for context. | Controls do not persist and do not affect delivery behavior. | No. | No persistence API. | Yes, disabled and labelled not connected. | Future delivery phase. |
| Subtitle/audio delivery settings | Placeholder | Intended controls are visible for context. | Controls do not persist and do not affect delivery behavior. | No. | No persistence API. | Yes, disabled and labelled not connected. | Future delivery phase. |
| Vault workflow | Hidden/removed | Historical docs state the old workspace was removed. | No routes, nav labels, CSS, or current behavior should be rebuilt. | Not applicable. | Not applicable. | No. | Keep removed. |
