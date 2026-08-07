# AGENTS.md

## Project Overview

Tuvima Library is a local-first media library system. It watches folders on disk, reads incoming files, identifies what they are, enriches them with metadata and relationships, stores normalized records in SQLite, and serves a dashboard for browsing, search, reading, settings, and review.

Important naming note: the product is branded as **Tuvima Library**, but most of the code still uses the older `MediaEngine.*` project and namespace names. In this repo, those names refer to the same product.

At runtime, the system is mostly split into two apps:

- `src/MediaEngine.Api` is the main Engine. It hosts the API, ingestion pipeline, database wiring, provider integrations, AI services, background jobs, health checks, and SignalR event collection.
- `src/MediaEngine.Web` is the Dashboard. It is the user-facing UI and talks to the Engine over HTTP and SignalR.

There is also a standalone `src/MediaEngine.Ingestion` worker host, but the main development/runtime path today is the Engine (`MediaEngine.Api`) plus the Dashboard (`MediaEngine.Web`).

## Tech Stack

- .NET 10 and C#
- ASP.NET Core Minimal APIs, hosted services, health checks
- Blazor Server with interactive server components
- MudBlazor for UI components
- SignalR for live updates from the Engine to the Dashboard
- SQLite for persistence
- Dapper for database access
- JSON file based configuration under `config/`
- Local AI via `LLamaSharp` and `Whisper.net`
- Media/file tooling via `TagLibSharp`, `VersOne.Epub`, `Xabe.FFmpeg`, and `SkiaSharp`
- Serilog for structured logging
- Polly / `Microsoft.Extensions.Http.Resilience` for outbound HTTP resilience
- Swagger / Swashbuckle for API docs
- Docker support plus Windows Service hosting
- `Tuvima.Wikidata` and Wikidata Reconciliation integration for identity and universe linking

## Local Dev Commands

- Work from the repo root: `C:\Users\shaya\OneDrive\Documents\Source\Repos\tuvima-library`
- This repo does not use npm/yarn for app startup. Dependency install is standard .NET restore.
- Required SDK: `.NET 10.0.100` from `global.json`
- Restore dependencies: `dotnet restore MediaEngine.slnx`
- Optional sanity build: `dotnet build MediaEngine.slnx`
- Repo-specific NuGet note: `nuget.config` maps `Tuvima.Wikidata*` packages to the local feed at `C:\Users\shaya\OneDrive\Documents\Source\Repos\tuvima-wikidata\artifacts`
- If restore fails for `Tuvima.Wikidata*`, check that sibling repo/feed path before changing package references

Start the two runtime apps in separate terminals:

- Engine: `dotnet run --project src/MediaEngine.Api`
- Dashboard: `dotnet run --project src/MediaEngine.Web`

Default local URLs:

- Engine: `http://localhost:61495` and `https://localhost:61494`
- Dashboard: `http://localhost:5016` and `https://localhost:7062`

Practical startup order:

1. Start `src/MediaEngine.Api` first and wait for `Now listening on: http://localhost:61495`
2. Start `src/MediaEngine.Web`
3. Open `http://localhost:5016`

Runtime notes:

- Before starting any development work, kill any running `MediaEngine.Api` and `MediaEngine.Web` `dotnet` processes first. This avoids locked binaries, stale runtime state, and confusing verification results.
- Run from the repo root so the Engine can resolve `config/`
- `src/MediaEngine.Api` launch settings set `TUVIMA_CONFIG_DIR=../../config` for normal `dotnet run`
- The Dashboard defaults to `Engine:BaseUrl = http://localhost:61495`
- If the Engine is started on a different address, set `TUVIMA_ENGINE_URL` before starting the Dashboard
- First Engine startup may benchmark hardware and download selected AI role models, which can take time and use about 6-7 GB with the default small-first catalog


## Current Dashboard/Product UI Model

The Dashboard is organized by user experience, not by a separate media management workspace:

- **Home** is discovery and overview. It uses the shared detail-derived rotating cinematic hero without an All/Read/Watch/Listen submenu; the first shelves overlap the hero's deep bottom fade so the artwork blends into the page. The full-viewport landing hero uses `DetailHeroContent` for the same logo/title scale, facts, primary action, progress, and full-paragraph synopsis as detail. Its top-anchored landscape backdrop extends beneath the translucent app bar, preserves the source's upper composition, and gives expendable lower artwork to a strong readability fade. Carousel context appears at top right as **Featured Content**, or as lane-specific Continue wording when the active item has progress. The non-control slide background opens the item's detail surface, while **Watch**, **Read**, **Listen**, **Play**, **Resume**, and **Restart** actions begin or control the experience directly. Watch landing slides for TV always use root show artwork, never an episode still; episode stills remain on episode-specific cards and details. Its shelf-driven rows remain ordered as Jump Back In, Watch, Read, Listen, Collections & Lists, then New in your library when data exists. New in your library is the lowest-priority placement and excludes structural work/group identities already visible in the preceding Home shelves; omit it when every candidate is already represented.
- **Read**, **Watch**, and **Listen** are the media lanes where users browse and experience their library. Only Home uses the premium cinematic landing hero. Each lane owns one compact `SurfaceNavigationBar` at the top across its Discover and scoped browse routes: Discover/Books/Comics for Read, Discover/Movies/TV Shows/Series for Watch, and Discover/Music/Audiobooks for Listen. Do not add a redundant lane-name/description block above that menu or repeat Read/Watch as a sidebar title. Lane roots are compact, row-based Discover surfaces with real route navigation; Read and Watch use the same persistent rail pattern as Listen. On desktop, the rail remains anchored to the viewport while only the adjacent lane content pane scrolls. Recommendation shelves may scroll and expose **View all**, while `/read/books`, `/read/comics`, `/watch/movies`, `/watch/tv`, `/watch/series`, `/listen/music`, `/listen/audiobooks`, and `/listen/playlists` immediately replace the content below that static navigation with the complete, filterable browse for the scope. Direct browse filters are an unboxed part of the page immediately beneath the lane menu: prominent search occupies its own row; `AppBrowseModeSelector` separates Browse as from filters; shared searchable multi-selects cover genre, person, and canonical year; `AppQuickFilterToggle` supplies large boolean targets; and the right-aligned Display group owns sorting, tile sizing, and Cards/List. `AppActiveFilterSummary` renders only non-default state. Browse modes and persistent-rail Browse as links must be generated from the same typed preset so the sidebar cannot drift from the toolbar. Query state uses readable `browse`, `q`, repeated `genre`, `person`, `year`, `status`, `sort`, `tile`, and `view` parameters. Filters apply to media before author, series, network, collection, or timeline grouping, and empty groups are omitted. Author, creator, director, artist, and narrator browse modes render the shared person-aware `MediaGroupTile`, preserve representative media artwork, show the managed headshot when available, and open the resolved person detail page. Every presentation surface derives contributor eligibility and ordering from the canonical contributor arrays/claims used by detail pages; the broader `person_media_links` enrichment graph must never promote assistant, supplementary, or otherwise non-primary relationships into browse groups, search attribution, person credits, or person-scoped collections. Selecting **Series only** must not replace or restyle the filter surface; the checked box alone conveys that state. Direct browse pages never require a second **View all** action and do not repeat media-type pill switches inside the filter toolbar. Music browse defaults to the Songs list: selecting a song starts playback directly, while its overflow menu can open the parent album. Album and Artist modes remain available, but tracks never receive standalone detail pages. Playlists appear once as their own Listen scope, not as a duplicate Music grouping.
- Lane-level groups are **shelves**: book series and comic volumes in Read, film series in Watch, and albums and audio series in Listen. Watch gives TV shows their own `TV Shows` shelf; they never share the movie `Series` shelf. The `Series` shelf contains only movie series that the Engine dynamically aligns from trusted library metadata, and its subtitle explains that behavior. TV shows may appear as shows with one or more owned episodes; both show and episode detail pages use the same owned-only, season-grouped episode list and never present provider-catalog episodes or totals as library ownership. Each owned episode has a show-scoped detail page opened from its still. A show targets the in-progress episode or the first owned episode (`Watch S1 E1` when that is the beginning). Before playback begins it may retain the series hero while showing the target episode's rating and runtime; after progress exists it switches to that episode's still and `Sx Ey` synopsis. The short provider/TMDB show synopsis belongs under the owned summary in a separated Series Description block. Movie heroes use the movie description without an episode heading. Detail heroes share one left-aligned identity/facts/action column, and at most two linked genres occupy their own non-wrapping line below the primary facts. Continue cards retain the episode playback asset and still, identify the child with copy such as `Continue · S5 E1`, open episode details from the card surface, and use episode-aware actions such as `Resume S5 E1`. Non-TV/non-music series shelves require at least two distinct owned works by default, configurable through `lane_group_display.*.minimum_series_items`. Single owned movie/book/audiobook titles stay visible as normal item tiles in their respective lanes rather than series cards; audiobooks belong in Listen, not Read. Ordered book/comic/movie series cards should show owned entries in sequence order through `DisplayCardDto.PreviewItems`; use decorative/randomized stacks only for real curated collections. A lane shelf must not be duplicated as a top-level Collections tile. Internal collection IDs may back these groups, but the user-facing label and route must follow the group type: a TV show is shown as a show, not as a collection.
- TV timeline groups use each show's canonical premiere year, never the newest owned episode year. Completed TV shows display the canonical premiere-to-finale year range when a trustworthy end year exists; never infer that range from the episodes the user owns. Network grouping cards keep the representative show artwork and add the resolved local network/streaming-service logo in the same prominent identity position used by person-group portraits.
- Non-TV series and collection containers use the dedicated fixed-size landscape `MediaGroupTile`. Rest is artwork-led: two, three, or four representative owned images form one slightly angled, overlapping cluster using actual portrait, square, or wide artwork metadata. Images retain their natural ratios, the card never grows or changes shelf geometry, and more than four children are never loaded or rendered. On Home and Discover shelves, hover or keyboard focus keeps the exact composition and dimensions, adds the purple boundary/glow, and fades in only a compact top overlay with light shading, container type, title, and one truthful status line. In direct tiled browse views, the title, year, and owned count are embedded into a restrained bottom gradient inside the card; the title is all caps, so source capitalization does not affect this treatment. Direct hover/focus adds only the glow plus a small open cue, with no type pill or full-card background replacement. The whole card is one group link. A collection with one exact person rule or an explicit person collection type may add that person's circular managed headshot and concise roles without moving or replacing the representative artwork; the whole card then opens `/details/person/{id}` rather than the collection detail. TV shows are the exception on Home and Discover: they use the standard `MediaTile` identity treatment, showing the show cover at rest and expanding to the show-level cinematic backdrop, logo, facts, and description on hover. A TV show must not receive the generic collection hover merely because it is structurally series-backed. Group hovers never become carousels, rotate child artwork, or provide child-level navigation. Static representative layouts belong in `MediaArtworkGroupPreview`; the retired `MediaArtworkCarousel` and rotating collage must not be restored. Shelf snap and stable-height logic must include both `.media-tile` and `.media-group-tile`. Individual media, TV shows, Continue, search, and other existing tiles remain on `MediaTile` or their purpose-built renderer.
- Every media or group card is one semantic navigation target that opens its detail surface. Cards do not expose inline Read, Play, Listen, My List, reaction, remove, or details buttons. Every vertically wrapping `MediaTileGrid` keeps cover/artwork unobstructed at rest and on hover. Individual items use a compact one-line title and optional year below the art on Discover shelves and direct browse grids; landscape series/collection groups keep their all-caps identity and truthful count inside the card without repeating an external caption. Keyboard focus or pointer hover preserves the exact geometry and content and adds only the stronger purple glow, with no background replacement or cinematic popover. TV Shows rows omit the redundant `TV Show` pill while retaining the owned episode count. Direct browse cards likewise omit context pills already supplied by selected lane navigation. Direct browse filter areas expose tile-size controls; tile width may change, but portrait, square, and landscape artwork ratios must remain fixed, and Music defaults smaller than books, comics, movies, TV, and audiobooks. Movie and TV cinematic landscape expansion is reserved for Home and lane Discover shelves when suitable background artwork exists; its left-aligned rating, classification, canonical year or year range, and runtime row uses only a compact translucent backing that hugs the text, and it remains action-free.
- **Collections** uses the same component-driven section shell as Read, Watch, and Listen: one compact, divider-free `SurfaceNavigationBar`, a viewport-anchored `MediaSectionShell` rail, and a scrolling content pane, with no landing hero. Its real routes are Discovery, Automatic, Curated, Shelves, and People. Automatic contains broader rollups only when a trusted shared series/franchise/universe relationship connects at least two distinct lower-level groups; a single item, album, show, or series never becomes an Automatic collection by itself. Curated collections are administrator-authored `Custom` collections, published library-wide by default, and only administrators can create or edit them; do not label them user-created. Collections **Shelves** opens as Read, Watch, and Listen preview lanes and then supports lane plus concrete Books, Comics, Movies, Albums, and Audiobooks filters. It indexes multi-work contributor groupings: Books by Author, Comics by Creator/Writer, Movies by Director, Albums by primary Artist, Audiobooks by Author, and Audiobooks by Narrator. A shelf requires at least two distinct owned top-level works after tracks collapse to albums, episodes collapse to shows where applicable, and format variants collapse to their canonical work. Duplicate local person identities with the same credited name collapse to one shelf, preferring the enriched identity and managed headshot. Albums, TV shows, and lane-level series/volumes are not themselves Collections shelves; they remain native groups in Read, Watch, and Listen. TV contributor shelves stay excluded until canonical show-level creator attribution is trustworthy. People is a paged list rather than a collection grid, supports search, role, and lane filters, and includes only canonical primary contributors on owned works. Multiple formats of one work, such as ebook plus audiobook, are variants and do not create a collection by themselves. Playlists remain only in Listen and never appear as a Collections category. Home's Collections & Lists shelf is placement-driven and profile-aware; do not synthesize top-level series/franchise tiles there.
- Ingestion must assign lane shelves even when Wikidata cannot resolve a QID, using the shared collection finalization path after quick hydration or retained-retail organization. Local/provider shelf identity is allowed for shelves; top-level Collections rollups still require trusted shared relationship rows.
- Sequence placement must be structural and media-aware. Use `ordinal_sort`, immediate shelf identity, `sequence_total`, `sequence_total_scope`, and child identity keys for ordering and counts; never add runtime title-specific fixes for individual books, comics, movies, albums, or shows. Comic UI shows issue identity and owned issue count without an `of N` completion target. Ownership donuts are reserved for authoritative finite/known containers; partial totals degrade to an owned-count badge.
- Provider sequence manifests use stable source container/member IDs and retain named missing entries. Container options merge only through shared identity evidence, never normalized display titles. Series detail shows the actual Series Set title, a selector only when alternate sets exist, a scoped Series selector only when multiple groups exist, and a Jump to selector when the active group has more than five entries. Canonical series containers reuse the numbered sequence rail on Overview. The canonical number stays above each cover; only proven consecutive positions receive a connector, and every connector paints behind the number nodes. The current item is indicated visually by its stronger purple frame glow, without a `This book`, `This movie`, or `Up next` footer; completion remains a separate check state and assistive technology receives `aria-current` context. Per-media missing-item defaults live only in `config/ui/library-preferences.json`; explicit per-profile, per-series overrides live in `profile_sequence_preferences`, and deleting an override restores config inheritance.
- All Dashboard selects use `AppSelect`, `AppIntSelect`, `AppMediaTypeSelect`, or the centrally styled `AppNativeSelect`; dropdown appearance and focus/open/disabled states belong to the shared control layer rather than page-local styling.
- Wikidata series manifests preserve `membership_scope`: positioned P179/direct P527 members are `MainSequence`, P361 members are `Supplementary`, expanded P527 children are `CollectedContent`, explicitly expanded franchise members are `BroaderContext`, and direct members lacking ordinal or chain evidence beside a positioned run are `Unpositioned`. Main-series totals count only `MainSequence` works; other rows remain visible in scoped detail groups, and source ordinals—including decimals—must never be densely renumbered.
- Comic issues may use a scoped series/run Wikidata QID when no issue-level Wikidata item exists. Store `wikidata_qid_scope = series` and `qid_resolution_method = comic_series_rollup`, label the source as series/run metadata in the UI, and keep issue titles/descriptions/cover art issue-scoped.
- **Search** is cross-library discovery. The global Search action and Ctrl+K open `UniversalSearchOverlay` above the current page without navigating. It debounces and cancels obsolete requests, traps focus, restores focus when closed, and shows ranked normalized media, people, series, collections, and playlists from `GET /api/v1/display/search`. Enter or **View all results** opens `/search`, whose URL-backed Media, Result type, canonical year, and sort filters preserve cross-media sections and the selected-result preview.
- **Detail pages** are where users view an item and fix/edit that item inline. Media, series, standard collections, and people use the canonical full-width `/details/{entityType}/{id}` surface. The desktop cinematic stage occupies `95svh`, exposing its lower navigation while minimizing crop of native 16:9 artwork, and its edge-to-edge, top-center landscape backdrop extends beneath both the translucent global app bar and the translucent `SurfaceNavigationBar` at the hero's foot. A strong bottom fade protects text and hands the page off to following content; a restrained top fade protects global navigation and a left scrim protects the identity column. Home and detail pages use the exact same `HeroBackdrop` foreground geometry: album art, cover art, and book page/spine decoration must not have page-specific sizes or implementations. Music albums use that same hero and navigation contract: the cover supplies a blurred atmosphere, a large sharp album/record composition is centered in the right half, identity and actions grow upward from the shared lower-left anchor, Play and visually distinct Shuffle actions share the primary row, and utilities remain on the second row. MusicAlbum exposes only **Overview** and **Details** in the shared `SurfaceNavigationBar`. Overview uses a proportional Tracks/Credits split, keeps the borderless track list and Show missing control without search, resolves identified artists into the same square clickable people cards used by movie and TV credits, and conditionally renders every other owned album from the exact canonical primary artist in a full-width **More by** shelf. Technical and source information belongs only to Details. Audiobook chapters remain a bounded internally scrolling module inside the stage; audiobooks retain chapter progress, direct starts, bookmarks, speed, history, sleep, and other audiobook playback tools, use a substantially larger right-side cover where viewport height permits, and do not gain a video-style whole-book Restart pairing. Audiobook Chapters remain embedded in the hero, and Editions is retired from all detail navigation. Structural details reserve the remaining stage for identity, then render the ordered sequence, collection, or owned-works array below it on Overview only. Standard collections use the neutral cinematic wash with no member-derived backdrop and show up to four representative owned artworks through the same `MediaArtworkGroupPreview` cluster used by collection tiles at a dominant hero scale. Their hero has no Open or lane action buttons; one functional Shuffle action opens or starts a random owned member, while a compact factual row shows the owned year range, total item count, and applicable Read/Watch/Listen counts. Book and comic covers share the same enlarged cover-only visual-height envelope; movies and TV without a true landscape backdrop use that generous foreground envelope as well. People use the larger portrait envelope and render one continuous page with no detail tab bar. A person uses the same title, role-credit, facts, and synopsis typography as media details; only the managed portrait treatment is person-specific. Linked identities use the same managed portrait-card treatment as cast and credits, without a redundant relationship subtitle beneath the linked person's name. When a collective pseudonym is explicitly credited, presentation and person attribution show the pseudonym alone; linked real identities remain relationships unless they are explicitly credited on another work. Real coauthors retain canonical order and compact bylines use `Author One; Author Two`. Living people show current age with Born; deceased people show age at death with Died. A person's owned music works collapse tracks into one album card and show the person's roles and year without a track count; neither artist nor song has a separate detail surface. No region overlaps or clips another. Role filters appear only with more than one real role, and Read/Watch/Listen filters appear only with more than one owned lane. Applicable selectors align left, Show missing and mixed-lane filters align right, and Jump to sits beside the array heading only above ten items. Every structural series array ends with a small owned summary and, only when the total is authoritative, missing/total text plus a simple progress bar. Overview follows the structural array and recommendations use a separate Related tab. A person's works use managed portrait cover/poster art or square album art rather than cinematic landscape backgrounds, and their identity must prefer the owned asset's canonical title over a parent collection label when the work-level title is absent. Standard collection details resolve membership through the same catalog rules as the Collections surface. Playlists remain specialized Listen queue/edit surfaces and do not use the shared detail route.
- **Review Queue** is only for blocked, uncertain, or low-confidence items that need human confirmation.
- **Settings/Admin** is for configuration and system operations: library folders, provider setup, profiles and roles, device/profile UI settings, ingestion status, system health, logs, and diagnostics.

The old all-in-one management workspace has been removed and must not be rebuilt. Do not add routes, navigation, docs, tabs, or a separate media-fixing workbench for it. Normal media corrections belong inline on the media surface where the issue appears, using `MediaEditorLauncherService` and `SharedMediaEditorShell`. Review uses the same shared editor in review mode.

Current UI entry points:

1. `src/MediaEngine.Web/Shared/MainLayout.razor` for the global shell, search, My List, unified activity indicator, permission-aware account menu, engine status, command palette, and persistent playback host. The account menu owns Needs Review attention; do not restore a standalone notification button.
2. `src/MediaEngine.Web/Components/Pages/LibraryBrowsePage.razor` for Home/discovery.
3. `src/MediaEngine.Web/Components/Cinematic/CinematicHeroCarousel.razor`, `CinematicHeroSurface.razor`, and `SurfaceNavigationBar.razor`, plus `src/MediaEngine.Web/Components/Details/DetailHeroContent.razor`, for Home/detail cinematic presentation and shared navigation.
4. `src/MediaEngine.Web/Components/Pages/ReadPage.razor` and `src/MediaEngine.Web/Components/Pages/WatchPage.razor` for the `/read` and `/watch` lane landing pages.
5. `src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor` for detailed Read/Watch/Listen browse behavior under tab routes.
6. `src/MediaEngine.Web/Components/Details/DetailPage.razor` and `src/MediaEngine.Web/Components/Universe/BookDetailContent.razor` for detail surfaces and inline edit launch points.
7. `src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor` for the exception queue.
8. `src/MediaEngine.Web/Components/MediaEditor/SharedMediaEditorShell.razor` for normal, review, and batch editing.
9. `src/MediaEngine.Web/Components/Settings/IngestionTasksTab.razor` for the Ingestion dashboard at `/settings/ingestion`; it uses the Engine `GET /ingestion/operations` snapshot plus SignalR ingestion progress.
10. `src/MediaEngine.Web/Services/Playback/PlaybackSessionController.cs` and `src/MediaEngine.Web/Components/Listen/ListenTransportControls.razor` for Listen playback state, typed commands, and shared transport controls.

Treat stale references to old all-in-one workspace components, retired CSS prefixes, broad management workbenches, or removed-workspace docs as cleanup candidates.

## Quality Gates and Regression Rules

- Do not recreate the old all-in-one management workflow. No new routes, implementation types, navigation labels, docs as current product behavior, or all-in-one media correction workbenches for it.
- Do not add backward compatibility, legacy fallback paths, compatibility readers/writers, migration shims, old route aliases, old config-key support, dual-schema support, or old workflow facades unless the user explicitly asks for that exact compatibility behavior. Prefer failing fast with a clear error or removing unsupported legacy state. If compatibility is explicitly approved, document the request, scope, sunset/removal criteria, and tests in the same change. Legacy database backup/reset is allowed as data safety, but not as in-place migration support.
- Normal detail-page fixes use `MediaEditorLauncherService.BeginInline` and replace only the cinematic hero at the same URL with a hero-constrained `SharedMediaEditorShell`; the lower Series/Overview/detail tabs remain mounted beneath it. Review and Batch reuse that workspace through `OpenAsync` dialogs. Keep normal Details lean (presentation overrides plus profile-local library preferences), keep provider facts read-only, and put structural parent moves in Matching.
- Single-item editing keeps metadata, local fields, and sorting in Details; it does not expose a separate Options tab. File shows physical-file state only, while History owns identity, metadata, artwork, and ingestion events. A retail rematch synchronously replaces provider-managed artwork and refreshes the detail hero before background Wikidata alignment proceeds.
- Listen playback UI should go through `PlaybackSessionController` state/commands and `ListenTransportControls`; browser-specific transport belongs behind the persistent Web audio host and `listenPlayback` JS bridge. Music shuffle/repeat, queue/history access, and audiobook speed/chapters/history/bookmarks/sleep tools remain available from the bottom player and popout. Music play counts are profile-scoped and qualify after 30 seconds of genuine listening, or 50% for tracks shorter than 30 seconds; forward seeks do not count as listening time.
- Review Queue is the exception workflow for blocked, uncertain, low-confidence, or unresolved items. Settings/Admin is for configuration and operational state, not a normal media correction workspace.
- Use `IDatabaseConnection.CreateConnection()` for normal repository, read-service, endpoint, background-job, and request-path database work. Dispose each short-lived connection with `using`.
- `IDatabaseConnection.Open()` is startup/schema/integrity-only. New uses outside `DatabaseConnection`, Engine startup, or explicitly documented test fixtures should fail guardrail tests.
- Current storage epoch is `guid-blob-v1`: internal SQLite GUIDs are 16-byte BLOBs, external IDs remain TEXT, API JSON still returns GUID strings, and legacy TEXT-GUID databases are reset/reingested rather than migrated in place.
- SQLite connections use WAL plus `synchronous=NORMAL`, `busy_timeout=5000`, memory temp storage, a 16 MiB page cache target, and a 256 MiB mmap cap for local ingestion throughput. The accepted tradeoff is that an OS/power crash can require reingesting the most recent file changes.
- Microsoft.Data.Sqlite does not provide asynchronous I/O. Repository APIs may
  remain `Task`-shaped, but SQLite/Dapper commands execute synchronously after a
  cancellation check; do not use their misleading async methods or wrap
  database work in `Task.Run`.
- Transactional writes use `IDatabaseConnection.ExecuteWriteAsync`. Only the
  global writer-semaphore wait is asynchronous; the callback is synchronous,
  bounded SQLite work that commits or rolls back before the lock is released.
- `canonical_values` is scalar-only. Multi-valued metadata belongs in `canonical_value_arrays`; do not reintroduce packed delimiter storage or compatibility readers.
- Provider/Wikidata/Wikipedia text shown in the Dashboard needs source attribution when available: provider name, source title, source URL, license, retrieved timestamp, and whether the displayed value was modified or summarized.
- UI comma-separated lists use standard English punctuation: no space before a comma and exactly one space after it. Linked genre pairs must preserve that grammar and remain together without wrapping between genres.
- Read, Watch, and Listen detail heroes use one shared identity column: logo or written title, sequence context, credits, and compact facts align to the left edge of the primary action button. Utility actions use icon-left-of-label controls, grow from the same left edge, and sit on the line below the primary action.
- The Engine detail composer selects a typed primary detail module; the Dashboard must not infer the module from route or tab names. A person's **Works in Your Library** contains only owned media from canonical eligible contributor credits, deduplicates each work, preserves all eligible roles on that work, and supports lane and role filtering. It must never expand into an external career filmography or use broad `person_media_links`.
- Detail navigation occupies a translucent shelf at the foot of the `95svh` cinematic stage so the backdrop continues visibly beneath it. Structural arrays use their own full-width surface below navigation and render only with Overview; Overview combines the full attributed description or biography, a purpose-built role-aware cast/credits peek, and series or collection context when applicable. Related content belongs in its own tab. People are the exception: biography, details, and owned works form one continuous page with no tab shelf. Music albums use **Overview** and **Details** tabs: Overview contains only the borderless track list, a right-side role-aware clickable credits rail, and the conditional full owned-album **More by** shelf, while source and technical detail content renders only under Details. Do not repeat the primary array in other tabs or Details. Audiobook chapters remain bounded and internally scrollable inside the hero; do not restore audiobook Chapters or Editions in shared detail navigation, Play All, track search, or a duplicate My List control inside a list.
- Home and detail heroes must compose `CinematicHeroSurface`/`CinematicHeroCarousel` with `DetailHeroContent`; do not create a second cinematic identity, facts, action, progress, or synopsis renderer. Read, Watch, Listen, and Collections do not render landing heroes. Home has no lane-scope submenu. Lane scope controls are real routes, while detail lower navigation uses `SurfaceNavigationBar`. The Home slide background opens details; group-opening actions such as **Open Show** use an open affordance, while direct media actions start playback or reading and progressed Home slides use the lane-specific Continue label plus Resume/Restart behavior where applicable. Keep item-specific detail route state and content unchanged when evolving navigation.
- Listen landing shelves are album-first. Do not emit individual music-track cards into Home or Listen landing rows; group newly ingested tracks into album cards and expose track identity through `DisplayCardDto.PreviewItems` and album/detail routes.
- UI display artwork should use managed Engine URLs or settled placeholders. Do not expose stale direct provider image URLs on lane cards, shelves, albums, detail pages, search results, or review cards.
- Avoid silent `catch { }` blocks. Best-effort failures need a justification comment or an explicit guardrail allowlist entry; user-visible failures need logging and degraded/error UI.
- Domain must stay independent of Web, API, Storage, Providers, Ingestion, Processors, AI, and UI packages. UI should consume view models/contracts/typed clients, not storage implementation models.
- `MediaEngine.Contracts` is the sole owner of product-defined types serialized
  over non-frozen HTTP or SignalR boundaries. API/Application/Domain models must
  cross through explicit boundary mappers; Dashboard-only wrappers may add
  presentation state but must not mirror or redefine the JSON contract.
- The only frozen wire exception is the exact Dashboard-local
  `LoreDeltaDiscoveredEvent` and `UniverseEnrichmentProgressEvent` SignalR pair.
  Universe HTTP DTOs are not exempt, and the exception must not gain types or
  fields.
- Preserve contract `[JsonPropertyName]` values, casing, defaults, nullability,
  collection shapes, and editor-required mutability. Contract completeness
  includes ingestion `count_unit`, person/collection roles, complete AI
  settings and `cpu_pressure`, and complete collection, playback/text-track,
  and plugin fields; do not reintroduce narrower API or Web mirrors.
- Keep `BoundaryContractGuardrailTests`, `WireContractSnapshotTests`, and
  focused contract shape/round-trip tests as ratchets. New boundary debt and a
  growing frozen exception are build failures.
- Domain aggregates expose child collections, property bags, and lifecycle state as read-only views. Mutate them through explicit aggregate methods (`Work.LinkToWikidata`, `Collection.SetVisibility`, `Collection.ChangeResolution`, and the named child methods), and keep repository hydration explicit instead of making aggregate internals public again. Persisted aggregate enums convert only through `AggregateStateSerializer`; unknown values fail fast.
- Razor components must not contain direct SQL. API endpoints should move SQL-heavy behavior into repositories/read services when touched.
- When product concepts, navigation, editing flows, database lifecycle, Docker startup, or CI checks change, update README/docs/AGENTS/CLAUDE and relevant `.agent` guidance in the same change.
- Every Dashboard code change requires visual validation of each affected surface at a representative desktop viewport (including 1920×1080 when layout scale is involved) in addition to automated tests. Capture the rendered state before and after changes when correcting a visual regression, and verify that responsive or lower-height rules do not override the intended desktop result.
- Before finalizing code changes, run at minimum `dotnet restore MediaEngine.slnx`, `dotnet build MediaEngine.slnx --no-restore`, and `dotnet test MediaEngine.slnx --no-build`. Run docs, Docker, format, and dependency checks when those areas are touched.
- For sequence/artwork ingestion fixes, validate with a fresh ingest rather than in-place repair. Stop Engine/Web, wipe only `C:\temp\tuvima-watch\books`, `C:\temp\tuvima-watch\audiobooks`, `C:\temp\tuvima-watch\tv`, `C:\temp\tuvima-watch\movies`, `C:\temp\tuvima-watch\music`, `C:\temp\tuvima-watch\comics`, and `C:\temp\tuvima-library`, then restart and inspect database/API results before visual Dashboard checks.

## Repository Map

### Main application code

- `src/MediaEngine.Api`
  - The main composition root.
  - Focused `DependencyInjection/Tuvima*ServiceCollectionExtensions.cs` modules
    register services, repositories, background jobs, health checks, SignalR,
    and API endpoints.
  - If you want to understand what the whole system starts and how parts are wired together, start here.

- `src/MediaEngine.Web`
  - The Blazor Server dashboard.
  - Uses typed HTTP clients to call the Engine API.
  - Uses SignalR for live progress and activity updates.

- `src/MediaEngine.Domain`
  - The shared language of the system.
  - Holds core aggregates, entities, enums, constants, shared configuration
    models, and inward-facing interfaces.
  - This project defines things like assets, editions, works, collections, universes, profiles, and the contracts other layers implement.
  - `Services/` also hosts the cross-cutting shared primitives every layer uses instead of re-implementing: `StringHelpers` (FirstNonBlank/FirstNonBlankOr), `MediaTypeParser` (canonical media-type alias table), `Hashing` (Sha256Hex, DeterministicGuid), `MediaMimeTypes`, `MediaEngineJson` (cached JsonSerializerOptions), and `EpisodePatterns` (shared SxxExx regex). Guardrail tests ban private re-declarations.

- `src/MediaEngine.Contracts`
  - The canonical HTTP and SignalR wire assembly shared by Engine and Dashboard.
  - Owns request, response, nested payload, and real-time event shapes; it does
    not own Dashboard presentation state or persistence projection rows.
  - Boundary mappers adapt internal models into these contracts when the shapes
    are not already identical.

- `src/MediaEngine.Storage`
  - SQLite repositories, embedded schema bootstrap, startup migrations, and config loading.
  - Every concrete repository belongs here. API services must not hide
    persistence behind `Store` or `DataService` names.
  - This is where persistent state lives: media records, review queue, activity log, provider cache, search index, reader progress, UI settings cache, and more.

- `src/MediaEngine.Ingestion`
  - The file intake pipeline.
  - Watches folders, debounces file system noise, hashes files, detects duplicates, runs processors, creates review items, and triggers organization/write-back work.

- `src/MediaEngine.Processors`
  - Reads actual media files.
  - Extracts metadata from EPUB, audio, video, comics, and generic files.
  - Also contains supporting media services like FFmpeg-based extraction and hero-banner generation.

- `src/MediaEngine.Intelligence`
  - Decides which metadata wins when sources disagree.
  - Handles scoring, fuzzy matching, identity matching, and collection arbitration.
  - The key mental model is "decision logic," not storage or UI.

- `src/MediaEngine.Providers`
  - Talks to outside metadata sources and runs the hydration pipeline.
  - Includes config-driven provider adapters, reconciliation, person enrichment, search, universe graph population, and deferred enrichment services.
  - A lot of provider behavior is driven by JSON config instead of hardcoded subclasses.

- `src/MediaEngine.AI`
  - Local model configuration, model downloads, model lifecycle management, hardware benchmarking, and AI-powered features.
  - Model roles are selected through `config/ai.json` `model_catalog` and `role_requirements`; prefer the smallest model that passes validation gates instead of promoting larger models because hardware is available.
  - Features include smart filename cleaning, media type advice, vibe tags, TLDR generation, taste profiling, description intelligence, cover-art checks, and Whisper-based audio work.

- `src/MediaEngine.Identity`
  - Profile management and multi-user rules.
  - Small project, but important for roles and user-facing account behavior.

### Supporting folders

- `tests/`
  - One test project per major source project.
  - Mirrors the production code layout closely.

- `config/`
  - Extremely important in this repo.
  - Holds core settings, libraries, providers, AI settings, scoring priorities, pipelines, hydration behavior, UI/device profiles, narration phrases, and secrets overlays.
  - Many behavior changes happen here without changing C# code.

- `docs/`
  - Product and architecture documentation.
  - Organized into tutorials, guides, reference, explanation, and architecture folders.

- `docker/`
  - Container entrypoint and packaging support.

- `scripts/docs/`
  - Documentation build and serve scripts.

- `tools/`
  - Local utilities and bundled support assets such as FFmpeg, reports, and test data.

- `assets/`
  - Branding images and screenshots.

## How The Parts Fit Together

Think of the product as a pipeline with a UI on top:

```text
Watch folders
  -> Ingestion
  -> File processors
  -> Intelligence/scoring
  -> Retail identification (Stage 1)
  -> Wikidata bridge resolution (Stage 2)
  -> Quick hydration
  -> Universe enrichment (Stage 3)
  -> SQLite + .data/assets + file organization + write-back
  -> API + SignalR
  -> Dashboard
```

In plain English:

1. A file appears in a watched folder.
2. The ingestion layer notices it, waits for file activity to settle, and makes sure it is not a duplicate.
3. File processors inspect the file itself and extract whatever metadata is already available.
4. Intelligence services decide the best current title/author/type values from the claims gathered so far.
5. Retail Identification (Stage 1) runs the configured provider chain for the media type. Music is configured as MusicBrainz first for canonical recording/release IDs, then Apple for artwork and retail metadata; other lanes use their configured retail/catalogue providers for cover art, descriptions, ratings, and bridge identifiers.
6. Wikidata Bridge Resolution (Stage 2) uses bridge IDs from Stage 1, such as ISBN, ASIN, TMDB ID, MusicBrainz ID, and Apple IDs, to find the canonical Wikidata entity (QID). This provides universe linkage, person relationships, and canonical metadata. If Stage 2 finds no QID, the item keeps its retail data and is flagged for periodic re-checking.
7. Quick Hydration gets the item visible with core identity, canonical values, and managed artwork.
8. Collection finalization assigns the item to its lane shelf and resolves any broader parent collection only when trusted shared relationships exist. This also runs for retained retail identities that have no Wikidata QID.
9. Universe enrichment (Stage 3) expands people, fictional entities, relationships, narrative roots, lyrics/subtitles, and additional artwork.
10. Storage writes the results into SQLite and stores managed artwork/headshots under `.data/assets/...` through `entity_assets` or the relevant person/entity table. Sidecars beside media files are optional exports only.
11. Organization and write-back services can move files into the library structure and write metadata back into the files.
12. The API exposes all of that state to the Dashboard.
13. SignalR broadcasts live events so the UI can update while ingestion and enrichment are happening.

Two-pass enrichment: The Quick pass gets the item visible on the Dashboard fast (core identity + cover art). The Universe pass runs later in the background for deep enrichment (relationships, people, fictional entities, additional images).

Canonical deep-dive: `docs/architecture/ingestion-identity-enrichment-pipeline.md`.

## A Non-Developer Guide To The Data Model

These are the most important concepts:

- **MediaAsset**
  - A real file you own on disk.
  - Example: one `.epub`, one `.m4b`, one `.mkv`.

- **Edition**
  - A specific release or format of a story.
  - Example: "First Edition Hardcover", "Director's Cut", "4K Blu-ray".

- **Work**
  - The underlying story or title, independent of format.
  - Example: the idea of *Dune* as a book or film title, not one specific file.

- **Collection**
  - The main grouping/browsing container for related works.
  - Depending on context, a collection may behave like a universe, a smart collection, a system list, a mix, or another discovery surface.

- **Universe**
  - A larger umbrella that can contain multiple collections.
  - Useful when several related groups belong to one broader narrative world.

- **Profile**
  - A user/persona with a role and preferences.
  - The Dashboard stores the active local profile in browser storage and uses that role to filter Settings navigation. The seed Owner Administrator remains the fallback.

- **Review Queue**
  - The safety net for uncertain matches.
  - If the system is not confident enough, it stores the item for human review instead of pretending it knows the answer.

The easiest way to explain the hierarchy is:

```text
file on disk -> MediaAsset -> Edition -> Work -> Collection -> Universe
```

Not every item reaches every level. Some files remain standalone if the system cannot confidently link them to a larger story world.

## What The Engine Actually Does At Startup

Reading `src/MediaEngine.Api/Program.cs`, the Engine does far more than expose HTTP endpoints. It also:

- opens SQLite and runs schema initialization plus startup migrations
- loads structured config from `config/`
- registers providers and HTTP clients
- wires up ingestion, scoring, hydration, search, and write-back services
- starts recurring background jobs
- sets up local AI model management and auto-download
- warms caches
- starts SignalR for real-time events
- exposes health checks and API endpoints

That file is the best "whole-system map" in the repository.

## Configuration Matters More Than Usual

This codebase is not only code-driven. A large amount of product behavior lives in JSON:

- `config/core.json` controls core paths and runtime behavior
- `config/libraries.json` describes watched libraries and media-type hints
- `config/providers/*.json` defines provider behavior
- `config/ai.json` defines local models, AI feature flags, and schedules
- `config/pipelines.json`, `config/scoring.json`, and `config/field_priorities.json` influence how metadata wins
- `config/ui/` controls palette, device profiles, and UI defaults
- `config/secrets/` overlays provider credentials

Before changing C# for behavior that looks configurable, check `config/` first.

## Best Entry Points For Reading The Code

If you are new to the repo, read these files in roughly this order:

1. `README.md`
2. `src/MediaEngine.Web/Shared/MainLayout.razor`
3. `src/MediaEngine.Web/Components/Browse/MediaBrowseShell.razor`
4. `src/MediaEngine.Web/Components/Details/DetailPage.razor`
5. `src/MediaEngine.Web/Components/Settings/SettingsReviewQueueTab.razor`
6. `src/MediaEngine.Api/Program.cs`
7. `src/MediaEngine.Api/DependencyInjection/`
8. `src/MediaEngine.Web/Program.cs`
9. `src/MediaEngine.Ingestion/IngestionEngine.cs`
10. `src/MediaEngine.Providers/Services/HydrationPipelineService.cs`
11. `src/MediaEngine.Intelligence/PriorityCascadeEngine.cs`
12. `src/MediaEngine.Storage/ConfigurationDirectoryLoader.cs`
13. `src/MediaEngine.Storage/DatabaseConnection.cs`

That sequence explains the product, the runtime wiring, the intake flow, the enrichment flow, the decision logic, and the persistence/config layer.

## Practical Reading Notes

- Ignore `bin/`, `obj/`, `.data/`, and `logs/` when learning the architecture.
- Expect product branding (`Tuvima`) and code naming (`MediaEngine`) to coexist.
- The Engine is the operational center of the app.
- The Dashboard is mostly a client of the Engine, not the place where core business rules live.
- Tests are organized by layer, so they are often the fastest way to confirm intended behavior once you know which project you are in.

## Current Collections and linked-identity clarifications

- Collections uses one viewport-bounded, padded content scroller beside its anchored rail; never allow a second document scrollbar.
- Automatic rollups prioritize trusted fictional-universe, franchise, and based-on/adaptation QIDs over lane-local parent/series structure. A cross-media identity such as Batman can therefore combine qualifying comic and film groups.
- People presence counts distinct top-level works: tracks collapse to albums and episodes collapse to shows.
- Person names use the exact shared media-detail title family, scale, weight, responsive density, and lower-left identity anchor through the rendered person copy hook. Person dates, locations, and owned-title facts reuse the standard detail metadata row/item typography used by year, runtime, and genre. The portrait may remain centered independently; it must not vertically center the identity column.
- Musical groups resolve all canonical Wikidata `has parts` identities into `person_group_members` and queue incomplete people for full hydration. Collective pen names continue to use aliases; band members must never be rendered as pseudonym aliases.



