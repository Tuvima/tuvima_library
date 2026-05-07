# Consumer Display API

The Engine owns consumer display composition for Home, Watch, Read, Listen, music landing pages, browse results, continue surfaces, search results, and grouped shelves. Web, iOS, Android, Roku, and TV clients should consume this API for browsing cards instead of reimplementing media-specific display rules.

## Endpoints

- `GET /api/v1/display/home`
- `GET /api/v1/display/browse?lane=watch&mediaType=Movie&grouping=all`
- `GET /api/v1/display/continue?lane=read`
- `GET /api/v1/display/search?q=dune`
- `GET /api/v1/display/shelves/{shelfKey}?lane=listen&mediaType=Music&grouping=home&cursor=...`
- `GET /api/v1/display/groups/{groupId}`

## Payload Rules

- Page-style endpoints return `DisplayPageDto`.
- Shelf pagination returns `DisplayShelfPageDto` with `nextCursor` and `hasMore`.
- Browse/search endpoints return cards in `catalog` by default to avoid duplicating the same cards in a `results` shelf.
- Clients that need a shelf-shaped browse response can pass `includeCatalog=false`.
- Detail/edit/playback APIs remain separate; display cards carry compact facts and semantic action targets, not full detail payloads.

## DTO Semantics

- `DisplayPageDto`: a client-neutral page with optional hero, shelves, and/or catalog.
- `DisplayShelfDto`: a named row of `DisplayCardDto` cards plus an optional see-all route.
- `DisplayCardDto`: compact card identity, title, facts, artwork, progress, flags, and semantic actions.
- `DisplayArtworkDto`: platform-neutral artwork variants and dimensions.
- `DisplayProgressDto`: progress percent, display label, last access timestamp, and resume action.
- `DisplayActionDto`: semantic action type such as `openWork`, `openCollection`, `playAsset`, or `readAsset`, plus IDs and optional web fallback URL.

## Client Responsibilities

Clients own layout and interaction style. Web can render hover cards, mobile can render long-press previews, and TV clients can render focus cards. All clients should use the same facts, artwork choices, progress, and action targets from the Engine.

