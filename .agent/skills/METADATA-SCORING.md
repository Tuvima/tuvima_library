# Skill: Metadata Scoring & Provider Management

> **Mirrors:** `CLAUDE.md` §3.2 (Weighted Voter) — keep both in sync per `.agent/SYNC-MAP.md`

> Last updated: 2026-03-01

---

## Purpose

This skill covers the Weighted Voter system — how metadata Claims are collected, scored, and resolved into Canonical Values — and how providers are managed.

---

## Key files

| File | Role |
|------|------|
| `src/MediaEngine.Intelligence/ScoringEngine.cs` | Groups claims by key, scores per field, computes overall confidence |
| `src/MediaEngine.Intelligence/ConflictResolver.cs` | Applies provider weights, stale decay, groups by value, detects conflicts |
| `src/MediaEngine.Intelligence/IdentityMatcher.cs` | Matches works to hubs via hard-ID short-circuit + fuzzy field average |
| `src/MediaEngine.Intelligence/HubArbiter.cs` | Selects the best Hub for a Work based on IdentityMatcher scores |
| `src/MediaEngine.Intelligence/Models/ScoringConfiguration.cs` | Thresholds: auto-link 0.85, conflict 0.60, epsilon 0.05, stale decay 90d/0.8 |
| `src/MediaEngine.Domain/Entities/MetadataClaim.cs` | Append-only claim record (key, value, confidence, provider, timestamp) |
| `src/MediaEngine.Domain/Entities/CanonicalValue.cs` | Scored winner per field per entity |
| `src/MediaEngine.Storage/Models/LibraryMasterManifest.cs` | Provider definitions, field weights, scoring settings |
| `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs` | Provider status + toggle endpoints |
| `src/MediaEngine.Web/Components/Settings/MetadataTab.razor` | Provider cards with weights and reachability |
| `src/MediaEngine.Web/Components/Settings/CuratorsDrawer.razor` | Manual claim locking and history |

---

## Scoring algorithm

```
For each metadata field (e.g., "title"):
  1. Collect all Claims for this field across all providers.
  2. For each Claim:
     a. rawWeight = Claim.Confidence * providerFieldWeight * staleDecayFactor
     b. staleDecayFactor = 1.0 if age < 90 days, else 0.8
  3. Normalise raw weights so they sum to 1.0.
  4. Group Claims by value (case-insensitive).
  5. Sum normalised weights per value group.
  6. Winner = group with highest total.
  7. If (runnerUp / winner) >= (1 - epsilon), mark as Conflicted.
  8. If any Claim is user-locked, it wins unconditionally (confidence = 1.0).
```

Overall entity confidence = mean of all field confidences.

---

## How to add a new provider

1. Create a class implementing `IExternalMetadataProvider` in `src/MediaEngine.Providers/`.
2. Assign a stable hardcoded GUID (never looked up from DB at runtime).
3. Add the provider entry to `tuvima_master.json` under `providers` with:
   - `name`, `version`, `enabled`, `weight`, `domain`, `capability_tags`, `field_weights`
4. Add the base URL to `provider_endpoints` in the manifest.
5. Register the class in `Program.cs` DI container.
6. The harvest pipeline will automatically include it in background enrichment.

No changes to existing providers or scoring code are needed.

---

## How to change trust weights

Currently: edit `tuvima_master.json` directly. No API endpoint or UI editor exists.

Future: a weight editor on the Metadata tab would allow runtime adjustment with live preview of scoring impact.

---

## How user corrections work

1. User opens Curator's Drawer from Server Settings.
2. Searches for a work and selects the correct match.
3. Clicks "Apply Correction" — this calls `PATCH /metadata/lock-claim` with `entityId`, `key`, and `value`.
4. Engine creates a new MetadataClaim with `IsUserLocked = true` and `Confidence = 1.0`.
5. Re-scoring runs; the locked Claim always wins, overriding all provider claims.
6. Locked Claims survive any future re-scoring and are written into the library.xml sidecar for portability.

---

## Known gaps

1. **Conflicted fields are not surfaced in the Dashboard.** The engine detects them but there is no UI indicator.
2. **Trust weights have no API endpoint or UI editor.** Must be changed via manifest file.
3. **Two providers (Local Filesystem, Open Library) have no reachability probe** — they always show as unreachable in the Metadata tab even though Local Filesystem is inherently always available.
4. **Scoring/Maintenance settings have no API surface.** Thresholds, decay parameters, and vacuum settings are manifest-only.
