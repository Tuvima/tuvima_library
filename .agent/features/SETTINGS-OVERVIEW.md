# Feature: Settings & Preferences

> Last audited: 2026-08-04 | Auditor: Codex

## Architecture summary

Settings use one route-driven shell at `/settings/{page}`. The left rail is a flat page list grouped as Personal, Administration, and Advanced; subsection controls belong inside the selected page. At narrow widths the rail becomes a page selector.

The canonical destinations are:

| Group | Pages |
|---|---|
| Personal | Profile, Playback & Reading |
| Administration | System Overview, Libraries, Ingestion, Metadata Providers, Review Queue, Activity & Audit, Playback & Delivery, Users & Access |
| Advanced | Local AI, Plugins, Developer Tools |

`/settings` redirects to `/settings/profile`. Legacy page aliases are normalized to the current canonical route. Privacy & Data is intentionally absent from navigation because the Engine does not expose the required history, tracking, export, or deletion operations yet.

## Interaction model

- Profile is one scannable dashboard: profile identity, appearance, activity summary, continue, recent history, and top genres. Missing activity renders an empty state, not invented zero metrics.
- Playback & Reading owns its General, Watching, Listening, Reading, and Subtitles tabs. Internal audiobook history thresholds live under an Advanced disclosure.
- System Overview consolidates library totals, ingestion, review, transcodes, recent runs, and operational health.
- Libraries, Ingestion, Metadata Providers, Activity & Audit, Playback & Delivery, Users & Access, Local AI, and Plugins use page-local tabs or segmented controls.
- Developer Tools is visible only to Administrators when internal tools are enabled. Provider and enrichment testers are launched from that page and are not duplicated in the rail.
- Unimplemented settings are hidden or explained as unavailable. The Dashboard does not present disabled sample controls as if they were saved configuration.

## Business rules

| # | Rule |
|---|---|
| SET-01 | Consumers see only Profile and Playback & Reading. |
| SET-02 | Curators see personal settings plus Review Queue and Activity & Audit. |
| SET-03 | Administrators see all configured settings groups; Developer Tools also requires its feature flag. |
| SET-04 | Navigation visibility is not an authorization boundary; Engine endpoints retain their own guards. |
| SET-05 | API-key plaintext is shown only at creation time. |
| SET-06 | The seed Owner and last Administrator protections remain enforced by the Engine. |
| SET-07 | Pages use real Engine, SQLite, or JSON-backed state and truthful unavailable/empty states. |

## Product owner summary

Settings now matches the product's intended information architecture: a short personal area, a complete administration area, and advanced tools kept out of the everyday path. Each page owns its own smaller tabs, and unsupported controls are no longer shown as fake settings.
