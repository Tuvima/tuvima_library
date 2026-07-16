---
title: "Inline Media Editing"
summary: "How Tuvima lets users correct media from the surface where they find the issue."
audience: "developer"
category: "architecture"
product_area: "editing"
tags:
  - "dashboard"
  - "editing"
  - "review"
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

For a single item, Details owns canonical metadata, local overrides, tags, ratings, notes, and sort fields; do not split those fields into an ambiguous Options tab. Batch editing may retain a separate Options step for bulk-only controls. File is limited to the physical file path and processing state. Identity, metadata, artwork, and ingestion events belong exclusively in History.

## After save

After an edit is applied, the current surface should refresh its data, keep the user in the same context, and show a clear success or failure message. Editing should not navigate away to a management workspace.

Applying a different retail match is a two-speed operation. The selected provider identity and its primary artwork are committed synchronously: stale provider-managed artwork is removed, the replacement cover is downloaded into managed storage, and the detail hero refreshes while the editor remains open. User-uploaded artwork remains available. Wikidata alignment and deeper enrichment are then queued and may finish in the background. The manual update and queued identity job must both be visible in application logs and item History.

In Review mode, resolving a row is explicit. Saving field changes, applying a provider/canonical match, or approving the current metadata must call the Engine review API for the concrete review item. If that call fails, the item stays in the Review Queue.

## Artwork editing

The shared editor owns artwork correction for normal and review flows. Each artwork type opens a focused gallery where one click makes a variant active, the magnifier opens a full-size preview, and the delete action removes that variant from the item with inline confirmation.

Deleting provider artwork removes the item association but keeps shared provider/image cache data when another work, edition, collection, or descendant still references it. Deleting user-uploaded artwork can also clean up the owned local file. Artwork type behavior is documented for users in [Artwork Types](../reference/artwork-types.md).


