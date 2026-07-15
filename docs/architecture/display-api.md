---
title: "Cross-Platform Display API"
summary: "Target-state display API notes for future clients that need common browse and card composition."
audience: "developer"
category: "architecture"
product_area: "api"
status: "target-state"
---

# Cross-Platform Display API

The Engine owns consumer display composition for browse surfaces. Web, mobile, TV, and future native clients should call the display API for shelves, cards, artwork choices, compact facts, progress, and semantic actions.

## Current Contract

The stable consumer routes are versioned under `/api/v1/display`:

- `/home`
- `/home?profileId=...`
- `/browse`
- `/continue`
- `/search`
- `/groups/{groupId}`

Display responses use platform-neutral DTOs from `MediaEngine.Contracts.Display`. They intentionally avoid web-only concepts such as CSS classes. Clients render their own layout while using the same card facts, typed badges, artwork variants, progress state, and action targets.

## Composition Boundaries

`DisplayComposerService` is the page and shelf orchestration layer. It should decide which shelves exist and how they are ordered.

Home composition is placement-aware: the Collections & Lists shelf comes from accessible collection placements at `location=home`, filtered by the active profile when `profileId` is supplied. The composer must not synthesize broad series/franchise cards for Home.

Lane grouping is item-count aware. TV show cards can represent a show with one or more owned episodes, but non-TV/non-music series cards require at least two distinct owned works by default. Operators can tune this with `config/ui/library-preferences.json` under `lane_group_display.*.minimum_series_items`. Grouped show/series cards should prefer collection/root artwork; episode stills are only a fallback when no show-level art exists.

TV Continue cards are episode cards, not substituted show cards. They retain the episode work/asset and managed still, send `Watch Sx Ey` or `Resume Sx Ey` to the player resolver, and expose the show-scoped episode detail route as a separate Details action.

Ordered series cards use `DisplayCardDto.PreviewItems` and `PreviewTotalCount` to expose the first owned works in display order. Clients should render those previews as an ordered strip with visible positions and should preserve order. Broader curated collections can still use decorative/randomized stacks because they are not sequence previews.

`DisplayProjectionRepository` owns database reads for display projections. It should keep SQL and visibility filtering out of page composition.

`DisplayCardBuilder` owns card, fact, badge, artwork, progress, and action construction. Hero facts should flow from the source card so spotlight and tile metadata stay consistent.

## Payload Size

Some page responses include the same cards in shelves and in `catalog`. Web uses `catalog` for browse grids, so it remains included by default for compatibility.

Clients that only need shelves can request smaller payloads with:

```text
includeCatalog=false
```

Example:

```text
GET /api/v1/display/browse?lane=watch&includeCatalog=false
```

This keeps the default web behavior stable while giving TV/native clients a way to avoid duplicated card payloads.

## Pagination

Browse endpoints already support `offset` and `limit` for catalog-style result pages. Lane pages currently return full shelf groups because that is simple for the web dashboard and current local-library sizes.

If TV or native clients need independent shelf paging, add shelf-level cursor routes instead of overloading page responses:

```text
GET /api/v1/display/shelves/{shelfKey}?cursor=...
```

The shelf route should reuse `DisplayCardDto` and the same `DisplayCardBuilder` rules so pagination does not fork display behavior.

## Active Non-Display Card Surfaces

The Universe card components, including `PosterSwimlane`, `PosterItemViewModel`, `LibraryCard`, `SquareCard`, and `LandscapeCard`, are still active in detail and related-content areas. They are not stale code.

Future cleanup can migrate related-content shelves to `MediaTile` or a display-card adapter, but detail and operational APIs should stay separate from display shelf APIs.

