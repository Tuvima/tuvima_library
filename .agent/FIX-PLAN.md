# Tuvima Library — Systematic Fix Plan

> Generated: 2026-03-01 | Based on: Full system audit across 7 feature areas

---

## How to read this plan

Every issue found during the audit is categorised into one of four tiers. **Work should be done in tier order** — Tier 1 issues block safe operation; Tier 4 issues are polish. Each item has a plain-English description, the business goal it serves, and the effort level.

| Tier | Meaning | When to do it |
|------|---------|--------------|
| **Tier 1 — Gating** | Security or data-integrity gaps that block exposing the Engine beyond localhost. | Before issuing any API keys to external tools. |
| **Tier 2 — Broken** | Features that are present in the UI but do not work. Users encounter dead ends. | Next development cycle. |
| **Tier 3 — Incomplete** | Features that work but are missing important pieces. | After Tier 2 is cleared. |
| **Tier 4 — Polish** | Minor inconsistencies, stubs, and forward-looking improvements. | When time allows. |

---

## Tier 1 — GATING (Security & Data Integrity) — COMPLETED (Phase A)

All five gating items were resolved in Phase A Security Foundation (2026-03-01).
Files created: `Security/RoleAuthorizationFilter.cs`, `Security/PathValidator.cs`, `Security/IntercomAuthFilter.cs`.
Files changed: `ApiKey.cs`, `schema.sql`, `DatabaseConnection.cs`, `ApiKeyRepository.cs`, `ApiKeyService.cs`, `Dtos.cs`, `ApiKeyMiddleware.cs`, `Program.cs`, plus all 7 endpoint files.
Migration: M-006 adds `role` column to `api_keys` table.

### G-01: Mandatory authentication on Engine endpoints — DONE
**What's wrong:** Every endpoint is accessible without an API key. The middleware only validates keys when they're voluntarily provided.
**Business goal:** Privacy, Reliability
**Fix:** Change `ApiKeyMiddleware` to reject requests without a valid `X-Api-Key` header. Exempt only: `GET /system/status` (health probe), Swagger (dev only), and same-origin Dashboard requests.
**Effort:** Small (1 file change + exempt list)

### G-02: Role-based authorization on sensitive endpoints — DONE
**What's wrong:** Profile roles (Administrator, Curator, Consumer) exist but are never enforced on any endpoint. A Consumer has the same API access as an Administrator.
**Business goal:** Privacy, Reliability
**Fix:** Associate API keys with a role at creation time. Add endpoint-level role checks for `/admin/*`, `/settings/*`, `/profiles/*`, and `/metadata/lock-claim`. Consumers should only access `/collections`, `/stream`, `/system/status`, and read-only metadata.
**Effort:** Medium (middleware + endpoint filters + key-role association)

### G-03: Path traversal protection — DONE
**What's wrong:** `/settings/test-path` and `/settings/folders` accept any filesystem path with no sandboxing. An unauthenticated caller can probe any directory or redirect the file watcher anywhere.
**Business goal:** Privacy, Reliability
**Fix:** After G-01 is in place (authentication required), add path validation to ensure submitted paths are within expected boundaries (e.g., under a configured root, or at least not system directories). Alternatively, rely on G-01 to prevent unauthorized access entirely.
**Effort:** Small (validation in 2 endpoint handlers)

### G-04: SignalR collection authentication — DONE
**What's wrong:** Any client can connect to `/intercom` and receive all broadcast events, including filesystem paths and library metadata.
**Business goal:** Privacy
**Fix:** Require a valid API key on WebSocket upgrade (already partially in place — the Dashboard sends `X-Api-Key` during connection). Add a collection filter or `IUserIdProvider` that validates the key.
**Effort:** Small (1 collection filter class)

### G-05: Rate limiting on sensitive endpoints — DONE
**What's wrong:** No rate limiting exists. Key generation, streaming, and ingestion can be called without throttling.
**Business goal:** Reliability
**Fix:** Register `AddRateLimiter()` with policies for key generation (e.g., 5/minute), streaming (e.g., 100/minute per key), and ingestion triggers (e.g., 10/minute).
**Effort:** Small (middleware config + policy definitions)

---

## Tier 2 — BROKEN (Users hit dead ends)

These are features that appear to work but lead to errors or no-ops.

### Phase B — Fix Dead Ends (B-04, B-05, B-06) — COMPLETED (2026-03-01)

Three quick-win items resolved:
- **B-04** — Deleted files now marked Orphaned via `FindByPathRootAsync` + `HandleDeletedAsync` rewrite.
- **B-05** — Conflict surfacing end-to-end: `is_conflicted` column (M-007), persistence in IngestionEngine + MetadataHarvestingService, `GET /metadata/conflicts` endpoint, ConflictsTab in Dashboard.
- **B-06** — Worker Host DI fix: 12+ missing registrations added to `Ingestion/Program.cs` + project reference to `MediaEngine.Providers` + `Microsoft.Extensions.Http`.

Files created: `Components/Settings/ConflictsTab.razor`, `Models/ViewDTOs/ConflictViewModel.cs`.
Files changed: `CanonicalValue.cs`, `IMediaAssetRepository.cs`, `ICanonicalValueRepository.cs`, `schema.sql`, `DatabaseConnection.cs`, `MediaAssetRepository.cs`, `CanonicalValueRepository.cs`, `IngestionEngine.cs`, `MetadataEndpoints.cs`, `Dtos.cs`, `MetadataHarvestingService.cs`, `MediaEngine.Ingestion.csproj`, `Ingestion/Program.cs`, `UIOrchestratorService.cs`, `ILibraryApiClient.cs`, `LibraryApiClient.cs`, `SettingsTabBar.razor`, `ServerSettings.razor`.

### B-01: Collection detail page does not exist
**What's wrong:** The Command Palette search navigates to `/collection/{collectionId}`, but no page exists at that route. Users land on the 404 page.
**Business goal:** Reliability
**Fix:** Create a Collection detail page at `Components/Pages/CollectionDetail.razor` showing the Collection's Works, Editions, and Media Assets. Requires `CollectionRepository.FindByIdAsync()` (B-02) and likely Work/Edition repositories (I-04).
**Effort:** Large (new page + repository methods + API endpoint)

### B-02: CollectionRepository.FindByIdAsync does not exist
**What's wrong:** There's no way to efficiently load a single Collection by ID. Required for the Collection detail page.
**Business goal:** Performance, Extensibility
**Fix:** Add `FindByIdAsync(Guid id)` to `ICollectionRepository` and `CollectionRepository`. Load the Collection with its full Work→Edition→Asset tree.
**Effort:** Small (1 interface method + 1 SQL query)

### B-03: Intent Dock filtering is disconnected
**What's wrong:** The four dock buttons (Collections, Watch, Read, Listen) render but clicking them has no effect — the `OnIntentChanged` callback is never wired.
**Business goal:** Reliability
**Fix:** Wire the callback in `MainLayout.razor` to filter the library view on `Home.razor`. Pass the active intent down to `UniverseStack` and `CollectionHero`.
**Effort:** Medium (state passing + filtering logic)

### B-04: Deleted files are not cleaned up — DONE
**What's wrong:** When a file is deleted from disk, the system logs it but never marks the asset as Orphaned in the database. No reconciler exists.
**Business goal:** Reliability
**Fix:** In `IngestionEngine.HandleDeletedAsync`, call `MediaAssetRepository.UpdateStatusAsync(assetId, AssetStatus.Orphaned)`. Also consider a periodic reconciler that scans for assets whose files no longer exist on disk.
**Effort:** Small (1 method call in existing handler) + Medium (optional reconciler)

### B-05: Conflict surfacing is missing — DONE
**What's wrong:** The Intelligence Engine detects when two sources disagree and can't pick a clear winner, but there's no UI to see or resolve these conflicts.
**Business goal:** Reliability
**Fix:** Add a "Conflicts" indicator to Collection tiles or a dedicated Conflicts panel in Server Settings. Show the entity, field, competing values, and a resolution button that links to the Curator's Drawer.
**Effort:** Medium (new UI component + API endpoint for conflicted entities)

### B-06: Standalone Ingestion worker host is broken — DONE
**What's wrong:** Missing 6+ dependency registrations from Phase 9. The DI container throws on startup.
**Business goal:** Reliability, Extensibility
**Fix:** Add the missing registrations (`IMetadataClaimRepository`, `ICanonicalValueRepository`, `IMetadataHarvestingService`, `IRecursiveIdentityService`, `ISidecarWriter`, `IMediaEntityChainFactory`, `ICollectionRepository`) to the worker host's `Program.cs`.
**Effort:** Small (DI registration additions)

---

## Tier 3 — INCOMPLETE (Works but missing pieces)

### I-01: Active profile is hardcoded
**What's wrong:** `ServerSettings.razor` defaults to "Administrator" for all users. `GetActiveProfileAsync()` returns the first profile. No session-based profile selection.
**Business goal:** Privacy, Reliability
**Fix:** Implement a profile selection mechanism (e.g., profile picker on Dashboard load, or PIN-based selection). Store the active profile in the Blazor circuit state and pass it through the layout.
**Effort:** Medium (selection UI + session state)

### I-02: Trust weight editing has no UI
**What's wrong:** Provider trust weights (the per-field numbers that control the Weighted Voter) can only be changed by editing the manifest JSON file by hand.
**Business goal:** Maintenance
**Fix:** Add an expandable weight editor to each provider card on the Metadata tab. Each field weight gets a slider (0–100%). Save calls a new `PUT /settings/providers/{name}/weights` endpoint.
**Effort:** Medium (UI slider grid + new endpoint + manifest write)

### I-03: Progress tracking (UserState) does not exist
**What's wrong:** The Hero tile's three progress bars (Watch, Read, Listen) always show 0%. The `UserState` entity exists in the domain but no API surface or tracking mechanism has been built.
**Business goal:** Extensibility
**Fix:** Build `IUserStateStore` implementation, a `UserState` API endpoint, and wire the progress data into `CollectionHero.razor`.
**Effort:** Large (new repository + API + UI wiring)

### I-04: Work and Edition repositories do not exist
**What's wrong:** `IWorkRepository` and `IEditionRepository` are not implemented. Cannot query Works or Editions independently of their parent Collection.
**Business goal:** Extensibility
**Fix:** Create `WorkRepository` and `EditionRepository` with standard CRUD operations. Required for the Collection detail page (B-01).
**Effort:** Medium (2 new repository classes)

### I-05: PersonEnriched event sends empty name
**What's wrong:** `MetadataHarvestingService.HandlePersonEnrichmentAsync` passes `Guid.Empty` to `GetByMediaAssetAsync`, gets 0 results, publishes the event with an empty person name.
**Business goal:** Reliability
**Fix:** Pass the correct Media Asset ID (from the `HarvestRequest`) instead of `Guid.Empty`. Or look up the person directly from the `HarvestRequest.EntityId`.
**Effort:** Small (1-line fix)

### I-06: CLAUDE.md Feature-Sliced layout is outdated
**What's wrong:** CLAUDE.md references deleted files (`FoldersTab`, `ProvidersTab`, `SecurityTab`) and doesn't mention the new page split (Preferences/ServerSettings) or the new tabs (MetadataTab, ApiKeysTab, UsersTab, etc.).
**Business goal:** Maintenance
**Fix:** Update Section 6 of CLAUDE.md to reflect the current file structure.
**Effort:** Small (documentation update)

### I-07: FoldersTab.razor is orphaned
**What's wrong:** Identical content to `LibrariesTab.razor` but not referenced by any page. Leftover from a rename.
**Business goal:** Maintenance
**Fix:** Delete `FoldersTab.razor`.
**Effort:** Trivial

### I-08: FileWatcher error recovery
**What's wrong:** Non-overflow `FileSystemWatcher` errors (e.g., network share disconnect) are swallowed silently. No recovery or notification.
**Business goal:** Reliability
**Fix:** Log the error, publish a SignalR event (e.g., `WatcherError`), and attempt to re-register the watcher after a backoff delay.
**Effort:** Small (error handler + retry logic)

---

## Tier 4 — POLISH (Minor improvements)

### P-01: Accent swatch highlight in light mode
**What's wrong:** `GeneralTab.razor` compares against `PaletteDark.Primary` even in light mode.
**Fix:** Check the active palette based on `ThemeService.IsDarkMode`.
**Effort:** Trivial

### P-02: Search is brute-force
**What's wrong:** `GET /collections/search` loads all collections into memory and filters. Won't scale to very large libraries.
**Fix:** Add a SQL `LIKE` or FTS query to `CollectionRepository` for server-side filtering.
**Effort:** Small

### P-03: Template preview saves as a side-effect
**What's wrong:** Clicking "Preview" on the organisation template also persists it to the manifest.
**Fix:** Separate the preview (read-only validation) from the save operation. Add a `POST /settings/organization-template/preview` endpoint that validates without persisting.
**Effort:** Small

### P-04: Work+Edition proliferation
**What's wrong:** Each ingested file creates a new Work+Edition chain, even if the same Work already exists under the same Collection.
**Fix:** Before creating a new Work, check if an existing Work under the same Collection has a matching title canonical value. If so, create only a new Edition under it.
**Effort:** Medium (matching logic in MediaEntityChainFactory)

### P-05: SidecarWriter is synchronous
**What's wrong:** `WriteHubSidecarAsync` and `WriteEditionSidecarAsync` use synchronous `XDocument.Save()` wrapped in `Task.CompletedTask`.
**Fix:** Use `XDocument.SaveAsync()` (available in .NET) for true async I/O.
**Effort:** Trivial

### P-06: HTTPS enforcement
**What's wrong:** No `UseHttpsRedirection()` in the pipeline. API keys over plain HTTP are in cleartext.
**Fix:** Add HTTPS redirect middleware and configure Kestrel for TLS.
**Effort:** Small

### P-07: API key expiration and rotation
**What's wrong:** Keys are valid forever with no expiration or last-used tracking.
**Fix:** Add `expires_at` and `last_used_at` columns. Update middleware to reject expired keys. Add UI to set expiration on key creation.
**Effort:** Medium

### P-08: Compact List view
**What's wrong:** Toggle button exists on Home page but list view is not implemented.
**Fix:** Create a list-style renderer for the Collection collection alongside the existing Bento grid.
**Effort:** Medium

### P-09: Universe grouping in Dashboard
**What's wrong:** Universe entity exists in domain but is never surfaced in the UI.
**Fix:** Group Collection tiles by Universe in the Bento grid. Add a Universe header row.
**Effort:** Medium

### P-10: Event scoping by role
**What's wrong:** All SignalR clients receive all events regardless of role.
**Fix:** Use SignalR groups or a collection filter to scope events by role (e.g., Consumers don't receive `FolderHealthChanged`).
**Effort:** Medium

---

## Recommended execution order

```
Phase A — Security Foundation (Tier 1) ✅ COMPLETE
  G-01 → G-02 → G-03 → G-04 → G-05

Phase B — Fix Dead Ends (Tier 2, quick wins) ✅ COMPLETE
  B-04 (deleted files)  ← Small
  B-05 (conflict UI)    ← Medium
  B-06 (worker host DI) ← Small

Phase C — Collection Detail Page (Tier 2, largest piece)
  B-02 (FindByIdAsync)  ← Small, prerequisite
  I-04 (Work/Edition repos) ← Medium, prerequisite
  B-01 (Collection detail page) ← Large

Phase D — Navigation Wiring (Tier 2)
  B-03 (Intent Dock)    ← Medium

Phase E — Complete Features (Tier 3)
  I-05 (PersonEnriched fix) ← Small
  I-06 (CLAUDE.md update)   ← Small
  I-07 (Delete FoldersTab)  ← Trivial
  I-08 (Watcher recovery)   ← Small
  I-01 (Profile sessions)   ← Medium
  I-02 (Weight editor UI)   ← Medium
  I-03 (Progress tracking)  ← Large

Phase F — Polish (Tier 4)
  P-01 through P-10 in any order based on preference.
```

---

## Impact summary

| Tier | Items | Business impact |
|------|-------|----------------|
| Gating (Tier 1) | 5 | Blocks external tool integration and LAN access |
| Broken (Tier 2) | 6 | Users encounter dead ends and missing features |
| Incomplete (Tier 3) | 8 | Working features missing important capabilities |
| Polish (Tier 4) | 10 | Minor improvements and future-proofing |
| **Total** | **29** | |
