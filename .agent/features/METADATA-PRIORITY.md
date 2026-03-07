# Feature: Metadata Priority (The Weighted Voter)

> **Mirrors:** `CLAUDE.md` §3.6 (Metadata Adapters) — keep both in sync per `.agent/SYNC-MAP.md`

> Last audited: 2026-03-01 | Auditor: Claude (Product-Led Architect)

---

## What the user sees

When a file is added to the library, Tuvima Library automatically fills in its title, author, year, cover art, and other details. Multiple sources may disagree — the file's own embedded data might say one thing, an online provider might say another. The user never has to resolve this manually unless the system genuinely cannot decide.

On the **Metadata tab** in Server Settings, the user sees every active source displayed as a card with:
- A traffic-light status dot (green = online, red = offline, grey = disabled).
- What it specialises in ("Expert in: narrator, series, cover").
- Its overall default trust level shown as a percentage.
- A breakdown of per-field trust levels (e.g., "narrator: 90%, author: 75%").
- A toggle to enable or disable the source entirely.

On the **Curator's Drawer** (accessible from Server Settings), the user can:
- Search for the correct identity of a work.
- Select the right match and lock it in — this creates a "user-locked" correction that permanently overrides all automated scoring.
- View the full claim history to see which sources contributed what data and when.

---

## How the voting works (plain English)

1. **Each piece of data is a "Claim."** When a file is scanned, the system records one Claim per data field per source. For example, the embedded metadata says the title is "Dune" (Claim 1), and Audnexus also says the title is "Dune" (Claim 2).

2. **Each source has a different trust level for each field.** Audnexus is highly trusted for narrator names (90%) but only moderately trusted for author names (75%). Apple Books is highly trusted for cover art (85%) but only moderately for titles (70%). These weights are configured in the manifest file.

3. **The election runs per field.** For each data field (title, author, narrator, etc.), the system tallies all Claims, adjusts each Claim's influence by the source's trust weight for that specific field, and picks the winner.

4. **Stale data decays.** Claims older than 90 days gradually lose influence (decay factor: 0.8). Fresh data is preferred.

5. **Close contests are flagged.** If the runner-up is within 5% of the winner, the field is marked as "Conflicted" and surfaced to the user for manual resolution.

6. **User corrections are final.** When a user locks a value via the Curator's Drawer, that Claim receives a confidence of 1.0 and is never overridden by any future re-scoring — regardless of what any provider says later.

7. **The winning values are stored as "Canonical Values"** — the single trusted answer used everywhere in the Dashboard.

---

## Business Rules

| # | Rule | Where enforced |
|---|------|----------------|
| MBR-01 | Every metadata Claim is append-only — historical Claims are never deleted. | Domain convention + schema design |
| MBR-02 | Each provider carries a per-field trust weight (not a single global weight). | Manifest (`field_weights` per provider) |
| MBR-03 | Stale Claims decay after 90 days with a factor of 0.8 (configurable in manifest). | Intelligence (ConflictResolver) |
| MBR-04 | A field is "Conflicted" when the runner-up's score is within 5% of the winner's. | Intelligence (ConflictResolver, epsilon = 0.05) |
| MBR-05 | User-locked Claims always win (confidence = 1.0, immune to re-scoring). | Intelligence (ScoringEngine) |
| MBR-06 | The auto-link threshold is 0.85 — files scoring below this are not auto-organised. | Intelligence (ScoringConfiguration) |
| MBR-07 | Provider enable/disable is a runtime toggle — no restart required. | Engine (SettingsEndpoints + ManifestParser) |
| MBR-08 | Adding a new provider requires only a manifest entry and a single new class — no existing code changes. | Architecture (IExternalMetadataProvider interface) |
| MBR-09 | Providers are never in the critical path — a failed network call returns empty Claims. The file stays in the library with its local metadata. | Ingestion (harvest queue design) |
| MBR-10 | Only titles, authors, and ASINs are sent to external services — no personal data or usage telemetry. | Architecture (provider adapter contracts) |

---

## Active Providers

| Provider | Domain | Zero-key? | Default Weight | Specialities | Reachability |
|----------|--------|-----------|----------------|-------------|--------------|
| Apple Books (Ebook) | Ebook | Yes | Varies by field | Cover (85%), Description (85%), Rating (80%), Title (70%) | Probed |
| Apple Books (Audiobook) | Audiobook | Yes | Same profile | Same as ebook variant | Probed |
| Audnexus | Universal | Yes | Varies by field | Narrator (90%), Series (90%), Cover (90%), Series Position (90%), Author (75%) | Probed |
| Wikidata | Universal | Yes | 1.0 for identity fields | QID (100%), Headshot (100%), Biography (100%) | Probed (1 req/sec throttle) |
| Local Filesystem | Universal | Yes | Inherent | File-embedded metadata | Not probed (always local) |
| Open Library | Universal | Yes | TBD | TBD — configured but not yet implemented | Not probed |

---

## Viewer vs. Admin experience

| Action | Consumer (Viewer) | Curator | Administrator |
|--------|-------------------|---------|---------------|
| See metadata on library cards | Yes | Yes | Yes |
| See which provider contributed a value | No (not yet surfaced) | Via Curator's Drawer claim history | Via Curator's Drawer claim history |
| Lock/correct a metadata value | No | Yes (Curator's Drawer) | Yes (Curator's Drawer) |
| Enable/disable a provider | No | No (admin-only tab) | Yes (Metadata tab toggle) |
| Change provider trust weights | No | No | Not yet — weights are manifest-only (no UI) |
| See Conflicted fields | Not yet surfaced in UI | Not yet surfaced in UI | Not yet surfaced in UI |

**Gap:** Conflicted fields (MBR-04) are computed by the Intelligence Engine but are not yet surfaced in the Dashboard. There is no visual indicator showing the user which fields need attention.

---

## Platform Health

| Area | Status | Notes |
|------|--------|-------|
| Weighted Voter scoring | **PASS** | Fully implemented in Intelligence layer. Per-field weights, stale decay, conflict detection all working. |
| User-locked Claims | **PASS** | Locking via Curator's Drawer creates confidence-1.0 Claims that survive all re-scoring. |
| Provider status display | **PASS** | Rich cards with trust weights, capability tags, domain badges, and reachability probes. |
| Provider toggle | **PASS** | Runtime enable/disable persisted to manifest and applied immediately. |
| Conflict surfacing | **FAIL** | Conflicts are detected but never shown to the user. No UI exists for conflict resolution outside the Curator's Drawer. |
| Trust weight editing | **WARN** | Weights are read-only in the UI. Changing weights requires editing the manifest file by hand. |
| Role enforcement on Metadata tab | **GATING REQUIRED** | The Metadata tab is visible to all Server Settings users, but the Engine endpoint has no role check — any API caller can toggle providers. |

---

## PO Summary

The Metadata Priority system — the Weighted Voter — is fully operational in the Engine. Each provider carries per-field trust weights, stale data decays over time, and user corrections are permanent and unbreakable. The Dashboard shows a clear, enriched view of every provider with live status indicators. **Two gaps remain: (1) Conflicted fields are computed but never shown to the user — there's no way to see which data needs manual attention, and (2) trust weights can only be changed by editing the manifest file directly — there's no in-app editor for them yet.**
