---
title: "Dashboard UI Architecture"
summary: "Current Dashboard structure, shell responsibilities, media lanes, inline editing, Review Queue, and Settings/Admin scope."
---

# Dashboard UI Architecture

The Dashboard is organized around discovery and media use, not a separate media management workspace. Home, Read, Watch, Listen, and Search are where users find and experience media. Detail pages and media rows/cards launch inline editing. Review Queue is the exception workflow for blocked or uncertain items. Settings/Admin is for configuration and system operations.

## Global shell

`src/MediaEngine.Web/Shared/MainLayout.razor` is the app-wide shell. It coordinates:

- MudBlazor providers.
- Brand/logo placement.
- Primary navigation.
- Global search.
- Review Queue notification.
- Profile switcher.
- Engine health/status copy.
- Command palette host.
- Global overlay host.
- Persistent Listen now-playing bar.
- Device context initialization.
- Keyboard shortcut registration and unregister on disposal.

Persistent playback stays in the shell so it survives navigation. Media editing does not live in the shell.

## Primary surfaces

- `/` is Home/discovery, rendered by `LibraryBrowsePage`.
- `/read` is the reading lane for books and comics.
- `/watch` is the movie and TV lane.
- `/listen` is the music, audiobook, album, artist, song, and playlist lane.
- `/search` is cross-library discovery.
- Detail pages show the selected media item and expose inline edit where appropriate.
- `/settings/review` is the Review Queue.
- `/settings` and `/settings/{Section}` are Settings/Admin.

`MediaBrowseShell` provides shared browse behavior for current media lanes. It may still use legacy-named helper components under `Components/Library`, but those helpers are reusable tables, columns, group pages, status pills, or batch controls. They are not a media library workflow.

## Inline editing model

The canonical edit path is:

```text
media surface -> MediaEditorLauncherService -> SharedMediaEditorShell
```

Normal mode is launched from detail pages, browse rows/cards, book details, listen tables, albums, tracks, movies, shows, and search/detail contexts. Review mode is launched from Review Queue. Batch mode is used only where a real selection exists.

After an edit is applied, the current surface refreshes and the user remains in the same context.

## Review Queue

Review Queue is for items that are blocked, uncertain, or need confirmation before ingestion/enrichment can continue. It should explain why each item needs attention and open the shared editor in review mode. Review may support dismiss, retry, skip-universe, approve, or apply-match actions according to Engine rules.

Review is not a broad Review Queue and must not become a second management workspace.

## Settings/Admin scope

Settings/Admin contains configuration and operational state:

- Library folders and organization templates.
- Provider configuration.
- Profiles, roles, and access.
- Device/profile UI configuration.
- Ingestion/task status.
- Engine/system health.
- Logs, diagnostics, storage, and runtime controls.
- Review Queue if navigation places review under Settings.

Settings/Admin should not host normal media browse/edit pages.

## Guardrails

- Do not add new media library routes, media library tabs, `media library-` CSS, or Review Queue docs.
- Do not restore `LibraryPage` or `LibrarySurfacePreset`.
- Do not route normal media fixes through a management workbench.
- Use `MediaEditorLauncherService` and `SharedMediaEditorShell` for normal, review, and batch edit flows.


