# Feature: Metadata Source Management

> **Mirrors:** `CLAUDE.md` §3.2 (Priority Cascade Engine), §3.7 (Hydration Pipeline) — keep both in sync per `.agent/SYNC-MAP.md`
> Last audited: 2026-04-02 | Auditor: Claude
> **Update 2026-04-02:** Unified scoring via `RetailMatchScoringService` — single scorer for both pipeline and manual search. Multi-author splitting, stronger Wikidata penalties (-35/-40), 85/15 score blend. Text-only Wikidata fallback removed; sentinel-only ResolveBridgeAsync calls blocked. Network metadata (P449) added. See `CLAUDE.md` §3.2 and §3.7 for current details.
> **Update 2026-03-30:** Provider management now uses ranked pipelines (`config/pipelines.json`) with three strategies (Waterfall, Cascade, Sequential) per media type. The fixed 3-slot system (`slots.json`) is auto-converted. Settings UI has a per-media-type strategy picker. See `CLAUDE.md` §3.7 for current details.

---

## What the User Sees

The **Metadata** tab in Server Settings presents all registered metadata sources — the services that enrich your library with cover art, descriptions, narrator credits, and more.

Sources are grouped by media type into collapsible categories:

| Category | Icon | What it covers |
|----------|------|----------------|
| **Ebooks** | Book icon | Sources that specialise in book metadata (e.g. Apple Books Ebook, Open Library) |
| **Audiobooks** | Headphones icon | Sources that specialise in audiobook metadata (e.g. Audnexus, Apple Books Audiobook) |
| **Movies** | Film icon | Sources that specialise in video metadata |
| **Universal** | Globe icon | Sources that work across all media types (e.g. Wikidata, Local Filesystem) |

Each source appears as a floating card showing:

1. **Status badge** — "Reachable" (green), "Unreachable" (red), or "Disabled" (grey)
2. **Source name** — bold display name
3. **Domain chip** — which media type this source covers
4. **Zero-key badge** — shown when the source requires no API key
5. **Capability icons** — visual indicators of what data this source provides (cover art, narrator, series, description, etc.)
6. **Trust Score** — a progress bar showing how much influence this source has when different sources disagree about the same detail
7. **Field-specific trust** — an expandable section showing per-field trust weights (e.g. "narrator: 90%, cover: 85%")
8. **Enable/disable toggle** — turn a source on or off instantly
9. **Drag handle** — visual indicator for future reordering (not yet functional)

### Add Source Wizard

Administrators can click the "Add Source" button to open a three-step wizard:

1. **Basic Details** — enter a source name, endpoint URL, and optional API key
2. **Advanced Mapping** — configure how the source's response fields map to Tuvima Library metadata keys (placeholder)
3. **Verify Connection** — test connectivity to the source endpoint (placeholder)

The wizard collects information in preparation for Engine-side custom source registration, which is planned for a future update.

### Edit and Delete

- **Edit** (pencil icon) — opens the wizard with the source's details pre-filled
- **Delete** (bin icon) — shows a confirmation dialog explaining that source removal requires Engine-side support

---

## Who Can See This

| Role | Can see tab? | Can toggle sources? | Can add/edit/delete? |
|------|-------------|--------------------|--------------------|
| **Administrator** | Yes | Yes | Yes |
| **Curator** | Yes | No (read-only) | No |
| **Consumer** | No (tab hidden) | — | — |

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| BR-01 | The Metadata tab is visible only to Administrators and Curators (AdminOnly = true in tab definitions). | Dashboard (SettingsTabBar) |
| BR-02 | Within the Metadata tab, write actions (toggle, edit, delete, add) require Administrator role. Curators see a read-only view. | Dashboard (MetadataTab + MetadataProviderCard) |
| BR-03 | Sources are grouped by Domain (Ebook, Audiobook, Video, Universal). Empty categories are hidden. | Dashboard (MetadataTab.GroupProviders) |
| BR-04 | Trust Score is displayed as a percentage derived from the provider's DefaultWeight. | Dashboard (MetadataProviderCard) |
| BR-05 | The Engine must be reachable for source data to load. If unreachable, a clear error message is shown. | Dashboard (MetadataTab.LoadProvidersAsync) |
| BR-06 | Source deletion is gated behind a confirmation dialog. Actual removal requires Engine-side support. | Dashboard (MetadataTab delete confirmation) |
| BR-07 | The Add Source wizard collects registration data but does not persist — custom source registration requires Engine-side support. | Dashboard (ProviderWizard) |
| BR-08 | Drag-and-drop reordering is visual only — no priority ordering until the Engine supports it. | Dashboard (MetadataProviderCard drag handle) |

---

## System Readiness

| Capability | Status | Notes |
|-----------|--------|-------|
| View grouped sources | **Live** | Sources load from Engine and display in category panels |
| Toggle source on/off | **Live** | Calls Engine PUT /settings/providers; refreshes list on success |
| Trust score display | **Live** | DefaultWeight and FieldWeights rendered from Engine data |
| Capability icons | **Live** | Mapped from CapabilityTags to Material Design icons |
| Status badges | **Live** | Derived from Enabled + IsReachable flags |
| Add Source wizard | **Stub** | Collects input; cannot register until Engine supports custom providers |
| Edit source | **Stub** | Opens wizard with pre-filled name; cannot save changes yet |
| Delete source | **Stub** | Confirmation dialog shown; actual deletion requires Engine support |
| Drag-and-drop reorder | **Stub** | Handle icon visible; no reordering logic until Engine supports priority |
| Connection test | **Stub** | Button exists; shows informational message about future availability |
| Schema mapping | **Stub** | Placeholder step in wizard; requires Engine support |

---

## Component Architecture

| Component | Responsibility |
|-----------|---------------|
| `MetadataTab.razor` | Page-level orchestrator: loads providers, groups by category, hosts wizard and delete dialog |
| `MetadataProviderCard.razor` | Single provider card: status, capabilities, trust score, toggle, edit/delete actions |
| `ProviderWizard.razor` | Three-step drawer wizard for add/edit flow |
| `SettingsTabBar.razor` | Tab navigation with role-based visibility |

---

## PO Summary

The Metadata Sources screen now presents your metadata providers in a clear, categorised layout — grouped by media type, with rich cards showing each source's capabilities, trust level, and status at a glance. Administrators can toggle sources on and off, and an "Add Source" wizard is ready to collect registration details for when the Engine supports custom provider connections. The screen is properly restricted: only Administrators and Curators can see it, and only Administrators can make changes.
