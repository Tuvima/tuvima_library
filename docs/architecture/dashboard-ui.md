---
title: "Dashboard UI Architecture"
summary: "Current Dashboard structure, shell responsibilities, media lanes, inline editing, Review Queue, and Settings/Admin scope."
audience: "developer"
category: "architecture"
product_area: "dashboard"
tags:
  - "dashboard"
  - "ui"
  - "editing"
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

## Listen Playback

Listen playback is coordinated through `PlaybackSessionController` in `Services/Playback`. UI components should read controller state, dispatch typed `PlaybackCommand` intent where possible, and let the persistent Web audio host execute `PlaybackTransportCommand` work against the hidden `<audio>` element.

`ListenTransportControls.razor` is the shared control surface for the bottom bar, persistent side panel, and popup. Do not duplicate play/pause, skip, previous/next, or chapter-control markup in those surfaces.

Browser-only behavior belongs in `wwwroot/app.js` behind the `listenPlayback` bridge and is configured by `config/ui/playback-client.json`. User-facing listening settings remain in the playback settings API.

## Primary surfaces

- `/` is Home/discovery, rendered by `LibraryBrowsePage`.
- `/read` is the cinematic reading lane landing page. `/read/books` and `/read/comics` render detailed browse tabs.
- `/watch` is the cinematic movie and TV lane landing page. `/watch/movies` and `/watch/tv` render detailed browse tabs.
- `/listen` is the music, audiobook, album, artist, song, and playlist lane.
- `/search` is cross-library discovery.
- Detail pages show the selected media item and expose inline edit where appropriate.
- `/settings/review` is the Review Queue.
- `/settings` and `/settings/{Section}` are Settings/Admin.

`LibraryBrowsePage` and `LaneLandingView` render cinematic spotlight-and-shelf discovery surfaces. `MediaBrowseShell` provides shared detailed browse behavior for current media lane tab routes. It may still use legacy-named helper components under `Components/Library`, but those helpers are reusable tables, columns, group pages, status pills, or batch controls. They are not a media library workflow.

## Sequence, Attribution, And Artwork Display

Lane pages, shelf cards, album pages, collection pages, detail pages, search
results, and review cards should use the same sequence and artwork contract from
the Engine:

- Sequence cards show one immediate shelf, ordered by `ordinal_sort`; decimal
  positions, comic annuals, TV specials, and multi-disc tracks should not
  collapse into the same child row.
- `Owned X of Y` uses `sequence_total` only when the total belongs to the
  displayed shelf, not a broader franchise or list article.
- Descriptions and metadata text sourced from Wikipedia, Wikidata, or providers
  should show attribution links on detail pages when attribution is present in
  the API response.
- Artwork should render from managed URLs or settled placeholders. The UI should
  not show broken external provider URLs directly.

## Inline editing model

The canonical edit path is:

```text
media surface -> MediaEditorLauncherService -> SharedMediaEditorShell
```

Normal mode is launched from detail pages, browse rows/cards, book details, listen tables, albums, tracks, movies, shows, and search/detail contexts. Review mode is launched from Review Queue. Batch mode is used only where a real selection exists.

After an edit is applied, the current surface refreshes and the user remains in the same context.

## Review Queue

Review Queue is for items that are blocked, uncertain, or need confirmation before ingestion/enrichment can continue. It should explain why each item needs attention and open the shared editor in review mode. Review may support dismiss, retry, skip-universe, approve, or apply-match actions according to Engine rules.

Review Queue is not a broad media management workspace and must not become one.

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

- Do not add all-in-one management routes, tabs, CSS prefixes, or docs that describe removed workflows as current product behavior.
- Do not restore removed all-in-one management implementation types.
- Do not route normal media fixes through a management workbench.
- Use `MediaEditorLauncherService` and `SharedMediaEditorShell` for normal, review, and batch edit flows.
- Refresh the current surface only after the shared editor returns a successful result; canceled edits should not mutate UI state.


