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
- A global Search icon in the top-right action group that opens the dedicated cross-library search page.
- A profile-aware **My List** bookmark route backed by the saved/favorites collection.
- A unified circular activity indicator for playback, ingestion, AI work, enrichment, and other durable operations; it becomes an idle check icon when no work is active.
- The account menu, including profile switching, Settings, Help, conditional sign-out, and permission-gated Needs Review count.
- Engine connection/degraded-state messaging.
- Command palette host.
- Persistent Listen now-playing bar.
- Device context initialization.
- Keyboard shortcut registration and unregister on disposal.

Persistent playback stays in the shell so it survives navigation. Media editing does not live in the shell.

The current navbar centers Read, Watch, Listen, and Collections independently of the logo and right-side actions. Search and My List are icon actions in the top-right group, active work is represented by one consistent progress surface, and review attention appears only as **Needs Review** inside the account menu. Consumer profiles do not fetch or render the review count. Sign out is shown only when OIDC or hybrid authentication is enabled; a local-only profile is switched rather than signed out.

The navbar brand uses the shared `assets/images/library.svg` lockup. The Dashboard project links that source file into its static web assets so the repository has one authoritative copy.

The activity surface is composed by `ShellActivityState`. It merges live SignalR ingestion, AI model-download, universe-enrichment, and durable `MediaOperationChanged` updates with scoped audio/video playback state. `GET /system/activity-status` supplies a sanitized active-operation snapshot so a newly opened Dashboard does not need to wait for the next SignalR event. Operation titles and filesystem paths are intentionally excluded from that shell endpoint.

## Listen Playback

Listen playback is coordinated through `PlaybackSessionController` in `Services/Playback`. UI components should read controller state, dispatch typed `PlaybackCommand` intent where possible, and let the persistent Web audio host execute `PlaybackTransportCommand` work against the hidden `<audio>` element.

`ListenTransportControls.razor` is the shared control surface for the bottom bar, persistent side panel, and popup. Do not duplicate play/pause, skip, previous/next, or chapter-control markup in those surfaces.

Browser-only behavior belongs in `wwwroot/app.js` behind the `listenPlayback` bridge and is configured by `config/ui/playback-client.json`. User-facing listening settings remain in the playback settings API.

## Primary surfaces

- `/` is Home/discovery, rendered by `LibraryBrowsePage` with a rotating cinematic hero and Read/Watch/Listen/Collections filters.
- `/read` is the cinematic reading lane landing page with Books, Comics, and genre filters. `/read/books` and `/read/comics` render detailed browse tabs.
- `/watch` is the cinematic movie and TV lane landing page with Movies, TV Shows, Series, and genre filters. `/watch/movies` and `/watch/tv` render detailed browse tabs.
- `/listen` keeps the permanent Listen rail and renders a compact square-art cinematic hero with Music, Audiobooks, and genre filters. Music shelves are album-first; individual tracks appear in album preview/detail content rather than as landing cards.
- `/collections` uses the same rotating hero and shared filter bar for broader rollups, personal collections, and playlists.

The landing carousel composes the same `DetailHeroContent` as item details, so the logo/title scale, structured facts, primary action, progress, and full-paragraph synopsis are identical. It adds only rotation controls and a top-right context label: **Featured Content** normally, or lane-specific Continue wording with Resume when progress exists. Group-opening actions such as **Open Show** use an open affordance rather than a play icon. The complete-frame backdrop remains uncropped, its true left edge is masked into the copy area, and Watch TV slides use the root show's managed background rather than an episode still.
- `/listen` is the music, audiobook, album, artist, song, and playlist lane.
- `/search` is cross-library discovery.
- `/my-list` is the active profile's saved shortlist.
- Detail pages show the selected media item and expose inline edit where appropriate.
- `/settings/review` is the Review Queue.
- `/settings` and `/settings/{Section}` are Settings/Admin.

`LibraryBrowsePage`, `MediaHubPage`, and `CollectionsPage` compose `CinematicHeroCarousel`. `CinematicHeroSurface` is the outer renderer for both the carousel and `DetailHero`, and both render identity/content through `DetailHeroContent`. `MediaHubPage` and `DetailTabs` compose `SurfaceNavigationBar`, keeping lane filters and detail sections visually identical while preserving their different item sets and state. The landing carousel owns rotation, reduced-motion behavior, and compact Listen density; it must not change detail-page route state or tab content. `MediaBrowseShell` provides shared detailed browse behavior for current media lane tab routes.

Desktop individual-item `MediaTile` previews expand the tile's flex item inside its existing shelf row. The resting cover is replaced at the same vertical position, neighboring cards shift horizontally, and leaving the preview restores the row. Media previews must not be portaled into a fixed overlay host or drawn over cards in another shelf.

Non-TV series and collection containers do not use that expansion behavior. `MediaGroupTile` is a dedicated wide, fixed-size landscape renderer. Its resting state is artwork-only. Two, three, or four representative owned images use approved layouts selected from the visible count and each image's real portrait, square, or wide metadata; images keep their natural ratios and only four are loaded and rendered. Hover or keyboard focus does not replace, reorder, or move the artwork and does not change card dimensions or shelf geometry. It adds the purple boundary/glow and fades in a compact top gradient containing only container type, a two-line-clamped title, and one status concept such as Continue, ownership, completion, or item/media-type count. The entire surface opens the full group. There are no buttons, arrows, dots, artwork rotation, selected-child state, or child-level actions. The shared shelf snap logic treats group tiles as first-class row items so arrows move between actual card boundaries instead of skipping to the row end. TV shows are an explicit exception: they render through `MediaTile` with the show cover at rest, then expand to the show-level cinematic backdrop, logo, facts, and description on hover. Owned episode navigation remains on the show detail surface. Ordinary media and Continue cards remain on their existing renderers.

The Watch landing keeps those concepts spatially separate. `TV Shows` is a dedicated shelf of show identities built from owned episodes. `Series` contains only movie series dynamically aligned by the Engine from trusted library metadata; the shelf subtitle communicates that automatic grouping, and its View all route opens the Movies tab in Series mode. TV shows are not repeated in Series.

`MediaArtworkGroupPreview` is the shared non-interactive representative-art primitive. Its adaptive group-card mode selects approved compositions by count and shape signature, preserves intrinsic image ratios with contain sizing, keeps missing-art fallbacks in the intended shape, and suppresses overflow badges. Its strip and clustered variants remain available for other artwork contexts. The previous `MediaArtworkCarousel` and JavaScript-rotating collage were removed and must not be reintroduced on card or detail surfaces; deliberate item browsing belongs in the existing detail lists and selectors.

TV show shelves use show-level cover art at rest; episode stills are reserved for the episode browser, episode details, and episode-specific Continue cards. An unstarted root TV detail may retain the enriched show backdrop or show cover while its facts and action target the first owned episode. Once episode progress exists, the root hero uses that episode's managed still and synopsis without removing artwork from the season list below it. Full-density landing and detail backdrops fill the viewport space between the app bar and shared lower navigation so the complete synopsis and navigation remain visible together; compact Listen density remains shorter. The foreground image box hugs the bitmap so its desktop fade begins at the bitmap's true left edge instead of at a viewport-relative position; mobile uses the same complete frame with a vertical fade into the content. TV episodes are grouped behind a styled season selector and rendered as a responsive still grid with each available synopsis. Hover or keyboard focus reveals a purple play affordance plus a still-wide Details overlay that opens the episode's show-scoped detail page.

Detail hero actions retain contextual verbs—Read for books/comics, Play for movies, `Watch Sx Ey` for individual TV episodes, and Listen for music/audiobooks—but cards do not duplicate those actions. Every media or group card is one semantic link to its detail surface, with no inline playback, reading, My List, reaction, remove, or details buttons. Book and comic portrait covers preserve the exact resting geometry on hover or keyboard focus and use only a thicker purple selection glow, with no hover text or scrim. Square music and audiobook covers also preserve their geometry and may use a compact media-appropriate identity strip. Movie and TV cards may retain cinematic landscape expansion when suitable background artwork exists; their left-aligned star rating, classification, year, and runtime row uses a compact translucent backing that hugs the text, and they remain action-free. Read, Watch, and Listen render their hero through the same component so the left edge, facts, synopsis, primary button, and utility controls use one layout contract; the identity slot alone may contain a downloaded logo or a written title. Compact facts use the same Read-scale typography, with at most two linked genres on a dedicated non-wrapping line below them. Read heroes show the first description paragraph and link to the full Overview when more text exists. A root show targets its in-progress episode or earliest owned episode, reports the owned episode count, and defaults to `Watch S1 E1` when that is the first owned item. Before playback, the series hero may remain while rating and runtime come from that target episode; TMDB episode runtime is preferred and owned-file duration is the fallback. After progress exists, the still and separated `Sx Ey: Episode title` synopsis follow that episode. The short provider/TMDB show description appears under the owned summary in a separated Series Description block. Movie heroes use the movie description without a heading. TV episode sequence items may use managed stills; movie, book, and comic sequences must use cover art rather than hero banners. Watch heroes retain a flat classification box and place a separator above the synopsis. Add to Collection remains a detail-page organization action and uses a distinct collection icon. Book detail artwork defines the height of its own page-edge and spine effects, so those decorations remain aligned when the cover scales. My List is the universal profile shortlist backed by the existing saved/favorites collection; Love remains a separate preference reaction. In-progress watch titles expose episode-aware Resume and Restart together. The watch utility row contains library, rating, collection, and editing tools without a redundant Show details action. Lane-specific tools such as Shuffle or Watch Party follow the universal actions. Detail backdrops use the same managed image in two roles: a non-repeating, full-bleed cover copy supplies the blurred atmosphere across the canvas, while an uncropped contain image preserves the complete enriched frame above it. Desktop applies a long reading scrim across the full canvas and reveals the sharp image from its intrinsic left edge; mobile switches the sharp image and scrim to a vertical treatment.

## Sequence, Attribution, And Artwork Display

Lane pages, shelf cards, album pages, collection pages, detail pages, search
results, and review cards should use the same sequence and artwork contract from
the Engine:

- Sequence cards show one immediate shelf, ordered by `ordinal_sort`; decimal
  positions, comic annuals, TV specials, and multi-disc tracks should not
  collapse into the same child row.
- `Owned X of Y` uses `sequence_total` only for an authoritative finite/known
  immediate container, never a broader franchise, partial manifest, or loaded
  row count. When that evidence is absent, the UI shows `X owned` without a
  missing count or completion donut.
- Comics show their issue identifier (`Batman · Issue 405`) and owned issue
  count without an `of N` completion target. Provider run totals remain internal
  matching and diagnostic facts because an ongoing comic run may keep growing.
- TV has show detail pages with show-scoped detail pages for each owned episode.
  Both surfaces share the same season selector and owned-only episode projection;
  provider-catalog rows and totals do not appear as library ownership. Seasons
  contain managed stills and available descriptions; Continue surfaces
  retain the episode still and playback target, use compact copy such as
  `Continue · S5 E1`, and keep `Resume S5 E1` separate from Details.
- Scoped series manifests render main-sequence works separately from
  supplementary short fiction and collected content. Exact source ordinals are
  displayed unchanged; an unnumbered supplemental work is labeled by scope and
  is not assigned an invented decimal or dense position.
- A media item with structural series placement exposes `Series` as its first
  detail tab and `Overview` as its second. The hero, Series tab, and hover card
  share concise placement copy such as `Book 1 in The Expanse` or
  `Movie 1 in The Lord of the Rings`; they do not append an `of N` total.
- Canonical book, comic, and movie series containers reuse that sequence rail
  directly on Overview. A source number is rendered above its cover, and a
  connector is drawn only when the neighboring stored positions are genuinely
  consecutive. Item details put `This book` or `This movie` inside the bordered
  current-item frame; completion is represented independently with a check.
- Structural shelf names remove a redundant trailing `Series` or `Collection`
  for presentation (`Dune Collection` becomes `Dune`). Curated collection
  names preserve those words because they are part of the collection identity.
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

Editor tab transitions are owned by `MediaEditorTabState`, including the return
from file inspection to the last content tab. Focused overlays such as artwork
preview belong in child components instead of adding more state and markup to
`SharedMediaEditorShell`.

Listen profile navigation JSON is decoded, reordered, and encoded through
`ListenPlaylistOrderState`. `ListenPage` should coordinate user intent and API
work rather than own the persistence format.

Dashboard Engine calls continue to be exposed through `IEngineApiClient`.
Feature-focused typed clients or `EngineApiClient.*.cs` partials own cohesive
endpoint families so the facade remains source-compatible while its
implementation is decomposed. AI endpoint methods are a separate feature area
and must not be moved as a side effect of non-AI client work.

The live ingestion dashboard separates three responsibilities. The primary
`IngestionLiveDashboardState` partial owns polling, subscriptions, cancellation,
and awaited shutdown; its `Projection` partial owns snapshot-to-view-model
calculation; and `IngestionDashboardSelectionState` preserves the selected batch
and stage when a live snapshot replaces the current rows. The Razor component
keeps markup separate from its code-behind. Its scoped styles live with
`IngestionTasksTab` because the parent owns the dashboard surface and reaches the
child through `::deep`; retired selectors should be deleted when markup is
removed.

Provider-priority presentation follows the same boundary: the tab coordinates
provider data in code-behind while `ProviderStageSelector` owns the accessible
stage choice control. The selector must continue to expose its active state with
`aria-pressed` rather than relying on color alone.

Retired Dashboard components are removed as complete closures: Razor, scoped
CSS, DI registrations, source-only test fixtures, and stale documentation. Do
not leave empty client or component marker types for hypothetical extraction;
introduce a focused type only when it has active behavior and consumers.

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


