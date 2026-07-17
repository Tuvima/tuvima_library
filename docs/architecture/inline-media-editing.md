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

- On detail pages, normal correction replaces the cinematic hero/detail surface with a full-width editor at the same URL. The global application shell remains visible.
- Review and batch correction continue to use the same editor workspace in a dialog because they originate outside a single detail-page context.
- Review Queue is only for blocked, uncertain, or low-confidence items that need human confirmation.
- Settings/Admin is for folders, providers, profiles, ingestion status, system health, logs, and configuration.
- No current feature should route users to a separate media library workspace.

## Shared editor

All media editing should launch through `MediaEditorLauncherService` and render `SharedMediaEditorShell`. Detail pages call `BeginInline`; Review and Batch call `OpenAsync`. `MediaEditorSurface` supplies the inline or dialog host without duplicating editor state or persistence logic.

The launch request should include the current context:

- `EntityIds`
- `LaunchEntityId`
- `LaunchEntityKind`
- `ActiveProfileId` for profile-owned notes, tags, visibility, and recommendation eligibility
- `Mode`
- `InitialScope` or `InitialTab`
- `MediaType`
- `HeaderTitle`
- `HeaderSubtitle`
- `CoverUrl`
- `PreviewItems`
- `ReviewItemId` plus the review-specific trigger/context when opened from Review Queue

Use `SharedMediaEditorMode.Normal` from media surfaces, `SharedMediaEditorMode.Review` from Review Queue, and `SharedMediaEditorMode.Batch` only when a real list/table selection exists.

For a single item, Details is intentionally small: Appearance contains display title, description, tagline where supported, and sort title; My Library contains profile-owned notes, local tags, visibility, and recommendation eligibility. Provider-managed people, dates, language, runtime, ratings, and genres remain visible as source facts and change through Matching or ingestion rather than becoming a large editable form. Batch editing may retain a separate Options step for bulk-only controls. Files is limited to physical-file state, while identity, metadata, artwork, and ingestion events belong exclusively in History.

TV shows and music albums expose Contents plus an aggregate Files summary. Episodes and tracks expose a deliberate Parent & position workflow inside Matching; structural moves are previewed and confirmed separately from ordinary metadata saves. Audiobook Contents exposes focused chapter-title overrides while file-derived chapter timing and boundaries remain read-only. Collection membership is edited only in the collection editor's Contents section, not repeated on the collection detail page.

Canonical presentation overrides are limited to `title`, `description`, `tagline`, and `sort_title`. The Engine atomically saves those work-level overrides together with profile-owned notes, tags, hidden state, and recommendation eligibility using an optimistic revision. A stale revision returns `409 Conflict`; the editor stays open, preserves the user's input, and offers an explicit reload action.

## After save

After an edit is applied, the current surface refreshes behind the editor and flips back only after the save succeeds. Cancel returns to the unchanged detail surface. Save failures and structural confirmations keep the editor visible. Dirty route changes are guarded, focus enters the editor on open and returns to the Edit action on close, and reduced-motion users receive an immediate swap instead of the 3D transition. Editing never changes the detail URL or navigates to a management workspace.

Applying a different retail match is a two-speed operation. The selected provider identity and its primary artwork are committed synchronously: stale provider-managed artwork is removed, the replacement cover is downloaded into managed storage, and the detail hero refreshes while the editor remains open. User-uploaded artwork remains available. Wikidata alignment and deeper enrichment are then queued and may finish in the background. The manual update and queued identity job must both be visible in application logs and item History.

In Review mode, resolving a row is explicit. Saving field changes, applying a provider/canonical match, or approving the current metadata must call the Engine review API for the concrete review item. If that call fails, the item stays in the Review Queue.

## Artwork editing

The shared editor owns artwork correction for normal and review flows. Each artwork type opens a focused gallery where one click makes a variant active, the magnifier opens a full-size preview, and the delete action removes that variant from the item with inline confirmation.

Deleting provider artwork removes the item association but keeps shared provider/image cache data when another work, edition, collection, or descendant still references it. Deleting user-uploaded artwork can also clean up the owned local file. Artwork type behavior is documented for users in [Artwork Types](../reference/artwork-types.md).


