---
title: "Inline Media Editing"
summary: "How Tuvima lets users correct media from the surface where they find the issue."
---

# Inline Media Editing

Tuvima no longer uses a separate media management workbench. Users browse through Home, Read, Watch, Listen, Search, and detail pages. When a media item needs correction, the edit action appears on that same surface and opens the shared media editor.

## Product model

- Normal corrections happen inline from cards, rows, albums, tracks, movies, shows, books, comics, and detail pages.
- Review Queue is only for blocked, uncertain, or low-confidence items that need human confirmation.
- Settings/Admin is for folders, providers, profiles, ingestion status, system health, logs, and configuration.
- No current feature should route users to a separate media library workspace.

## Shared editor

All media editing should launch through `MediaEditorLauncherService` and render `SharedMediaEditorShell`.

The launch request should include the current context:

- `EntityIds`
- `LaunchEntityId`
- `LaunchEntityKind`
- `Mode`
- `InitialScope` or `InitialTab`
- `MediaType`
- `HeaderTitle`
- `HeaderSubtitle`
- `CoverUrl`
- `PreviewItems`
- `ReviewItemId` plus the review-specific trigger/context when opened from Review Queue

Use `SharedMediaEditorMode.Normal` from media surfaces, `SharedMediaEditorMode.Review` from Review Queue, and `SharedMediaEditorMode.Batch` only when a real list/table selection exists.

## After save

After an edit is applied, the current surface should refresh its data, keep the user in the same context, and show a clear success or failure message. Editing should not navigate away to a management workspace.

In Review mode, resolving a row is explicit. Saving field changes, applying a provider/canonical match, or approving the current metadata must call the Engine review API for the concrete review item. If that call fails, the item stays in the Review Queue.


