# Skill: Settings Management

> **Mirrors:** `CLAUDE.md` §3.11 (Configuration Architecture) — keep both in sync per `.agent/SYNC-MAP.md`

> Last updated: 2026-03-01

---

## Purpose

This skill covers all actions related to reading and modifying Tuvima Library settings — both user Preferences and server-level configuration.

---

## Key files

| File | Role |
|------|------|
| `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs` | Engine: folder, provider, and template endpoints |
| `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs` | Engine: API key and provider config endpoints |
| `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs` | Engine: profile CRUD endpoints |
| `src/MediaEngine.Api/Models/Dtos.cs` | Engine: all request/response shapes |
| `src/MediaEngine.Storage/Models/LibraryMasterManifest.cs` | Manifest: persistent settings store |
| `src/MediaEngine.Ingestion/Models/IngestionOptions.cs` | Options: ingestion runtime config |
| `src/MediaEngine.Ingestion/FileOrganizer.cs` | Template validation + file move logic |
| `src/MediaEngine.Web/Components/Pages/Preferences.razor` | Dashboard: user preferences page shell |
| `src/MediaEngine.Web/Components/Pages/ServerSettings.razor` | Dashboard: admin settings page shell |
| `src/MediaEngine.Web/Components/Settings/SettingsTabBar.razor` | Dashboard: tab navigation + role filtering |
| `src/MediaEngine.Web/Components/Settings/GeneralTab.razor` | Dashboard: profile + appearance |
| `src/MediaEngine.Web/Components/Settings/LibrariesTab.razor` | Dashboard: folder paths + org template |
| `src/MediaEngine.Web/Components/Settings/MetadataTab.razor` | Dashboard: provider status + toggles |
| `src/MediaEngine.Web/Components/Settings/ApiKeysTab.razor` | Dashboard: guest key management |
| `src/MediaEngine.Web/Components/Settings/UsersTab.razor` | Dashboard: profile management |
| `src/MediaEngine.Web/Components/Settings/CuratorsDrawer.razor` | Dashboard: metadata correction panel |
| `src/MediaEngine.Web/Services/Integration/ILibraryApiClient.cs` | Dashboard: API client contract |
| `src/MediaEngine.Web/Services/Integration/LibraryApiClient.cs` | Dashboard: API client implementation |
| `src/MediaEngine.Web/Services/Integration/UIOrchestratorService.cs` | Dashboard: orchestrator pass-through |

---

## How to modify settings

### Adding a new settings tab

1. Add a new value to the `SettingsTab` enum in `SettingsTabBar.razor`.
2. Add its `TabDefinition` entry in the `_allTabs` list (icon, label, admin-only flag, category).
3. Create the tab component in `src/MediaEngine.Web/Components/Settings/`.
4. Add a `case` in the parent page (`Preferences.razor` or `ServerSettings.razor`) `@switch` block.
5. If it needs Engine data, add methods to `ILibraryApiClient` + `LibraryApiClient` + `UIOrchestratorService`.

### Adding a new Engine settings endpoint

1. Add the DTO(s) to `src/MediaEngine.Api/Models/Dtos.cs`.
2. Add the endpoint handler in `SettingsEndpoints.cs` (or `AdminEndpoints.cs` for admin-level operations).
3. Register the route in the appropriate `Map*Endpoints()` extension method.
4. Add the client method to `ILibraryApiClient` and `LibraryApiClient`.
5. Add the pass-through to `UIOrchestratorService` if the Dashboard tab calls it via the orchestrator.

### Modifying provider trust weights

Currently manifest-only. Edit `tuvima_master.json` → `providers` → target provider → `field_weights`. No API endpoint exists for this yet.

---

## Validation rules

| Setting | Validation | Location |
|---------|-----------|----------|
| Display name | Max 50 chars, non-empty | Dashboard (GeneralTab text field) |
| Profile role | Must be "Administrator", "Curator", or "Consumer" | Engine (ProfileEndpoints) |
| Folder paths | Probed for existence, read, and write access | Engine (SettingsEndpoints test-path) |
| Organisation template | Sample-based resolution; rejects empty parens, double spaces, double separators | Engine (FileOrganizer.ValidateTemplate) |
| API key label | Non-empty | Engine (AdminEndpoints) |
| Provider name | Case-insensitive lookup against manifest entries | Engine (SettingsEndpoints) |
| Provider secret write | Rejects the '********' masked sentinel value | Engine (AdminEndpoints) |

---

## Known gaps

1. **No Engine-side role enforcement** — all endpoints are open.
2. **Active role is hardcoded** to "Administrator" in `ServerSettings.razor`.
3. **FoldersTab.razor is orphaned** — identical content to LibrariesTab.razor but unreferenced.
4. **Template preview has a save side-effect** — "Preview" also persists the template.
5. **Accent swatch highlight** only checks `PaletteDark` — may not work correctly in light mode.
6. **Error handling inconsistency** — settings methods in the API client log + set LastError; admin/profile methods silently swallow exceptions.
