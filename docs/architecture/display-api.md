# Cross-Platform Display API

The Engine owns consumer display composition for browse surfaces. Web, mobile, TV, and future native clients should call the display API for shelves, cards, artwork choices, compact facts, progress, and semantic actions.

## Current Contract

The stable consumer routes are versioned under `/api/v1/display`:

- `/home`
- `/browse`
- `/continue`
- `/search`
- `/groups/{groupId}`

Display responses use platform-neutral DTOs from `MediaEngine.Contracts.Display`. They intentionally avoid web-only concepts such as CSS classes. Clients render their own layout while using the same card facts, artwork variants, progress state, and action targets.

## Composition Boundaries

`DisplayComposerService` is the page and shelf orchestration layer. It should decide which shelves exist and how they are ordered.

`DisplayProjectionRepository` owns database reads for display projections. It should keep SQL and visibility filtering out of page composition.

`DisplayCardBuilder` owns card, fact, artwork, progress, and action construction. This keeps media-specific presentation rules in one reusable place.

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

Future cleanup can migrate related-content shelves to `DiscoveryCard` or a display-card adapter, but detail and operational APIs should stay separate from display shelf APIs.

