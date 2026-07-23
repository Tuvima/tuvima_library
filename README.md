<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/images/tuvima-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/images/tuvima-logo.svg">
  <img src="assets/images/tuvima-logo.svg" alt="Tuvima Library" height="90" />
</picture>

**A private, local-first library for the stories you already own.**

*Tuvima Library watches your folders, identifies your books, audiobooks, movies, TV shows, music, and comics, enriches them with metadata, and presents them by story instead of by file type.*

<br/>

[![License: AGPLv3](https://img.shields.io/badge/License-AGPLv3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-8b5cf6.svg)](https://tuvima.github.io/tuvima_library/)
[![Status](https://img.shields.io/badge/status-Early%20Access-f0ad4e.svg)](https://tuvima.github.io/tuvima_library/product/status/)

</div>

---

## What Tuvima Is

Tuvima Library is an Early Access media library for people with real collections on disk. It is built for the person whose *Dune* ebook, audiobook, film, soundtrack, and comics should feel like one library, not six disconnected apps.

The Engine runs locally, watches the folders you choose, reads incoming files, identifies what they are, enriches them with metadata and artwork, stores normalized records in SQLite, and serves a Blazor Dashboard for browsing, search, reading, playback, settings, and review.

The product is branded as **Tuvima Library**. The code still uses `MediaEngine.*` project and namespace names in many places; in this repository, those names refer to the same product.

## Why It Matters

- **Story-first organization:** Home highlights what to resume, then Watch, Read, Listen, placed Collections & Lists, and new arrivals. Home alone uses the premium cinematic hero. Read, Watch, and Listen keep a compact Discover/media navigation bar at the top while the content underneath changes; redundant lane identity blocks and sidebar lane titles are omitted. Their Discover surfaces are compact, rail-navigated, and row-based; direct routes such as `/read/books`, `/read/comics`, `/watch/movies`, `/watch/tv`, `/watch/series`, `/listen/music`, and `/listen/audiobooks` open the complete filterable library beneath that same navigation in one click and use vertically wrapping tiled results rather than horizontally scrolling media rows. Direct filters separate Browse as, Filter by, and Display; use one URL-backed configuration for both toolbar choices and sidebar shortcuts; and filter matching media before creating author, series, collection, network, or timeline groups. Author, creator, director, artist, and narrator views use the same landscape person-collection card, including representative titles, a managed portrait, role, year span, owned count, and direct navigation to that person's detail page. Those person views, search attribution, person details, and person-scoped collections all use the same ordered canonical credits as the media detail page, so broad enrichment relationships such as assistant directors do not become top-level library credits. Direct tiled results keep their cover or group artwork unchanged, omit redundant context pills such as `TV Show` within TV Shows, and use a purple hover/focus glow; individual items show a compact title/year below the art, while landscape series/collection cards embed an all-caps title, year, and count inside the card. Each filter area can resize tiles while the artwork ratio remains fixed. Cinematic background expansion remains on Home and Discover shelves. Listen is album-first: `/listen/music` opens the filterable album grid at a smaller default tile size, while Songs uses the shared artwork-led audio table with play, queue, drag-to-playlist, resizable columns, and profile-scoped qualified play counts. Ingestion creates lane shelves from trusted QIDs, provider IDs, or local grouping metadata; Collections appear only when a broader world spans multiple shelves. Series and collection cards use one fixed-size landscape treatment with two to four slightly angled representative covers; person-scoped collections add the linked creator's portrait and open that person's detail page.
- **Cinematic continuity:** Landing and detail heroes share the same identity/content composition—logo scale, facts, action, progress, and full-paragraph synopsis—plus the same uncropped backdrop, left-edge fade, atmosphere, and contrast layers. Carousel context sits at the top right as Featured Content or lane-specific Continue wording. Watch landing TV slides always use show-level managed artwork rather than episode stills, and detail tabs use the same lower navigation bar as lane filters.
- **Local-first privacy:** Your files, database, models, and processing stay on your machine. Provider calls are only for metadata lookups that you configure.
- **Honest automation:** Strong matches flow through automatically. Low-confidence, blocked, or uncertain items go to the Review Queue instead of being silently misfiled.
- **One dashboard:** Home, media lanes, Search, detail pages, Settings/Admin, and Review Queue work together instead of forcing every correction into a separate management workspace. Global Search opens an instant overlay over the current page and returns ranked titles, people, series, collections, and playlists; View all opens a URL-backed, sectioned search page with a contextual preview. TV show and episode detail pages share the same season selector and list only episodes present in the library. An unstarted show may keep its series hero while its facts and Watch action target the first owned episode; once progress exists, the hero follows that episode and uses its `Sx Ey` synopsis. The short provider show description remains in a separated Series Description block. Detail heroes share one left-aligned identity/facts/action column, use common button geometry and typography, and show at most two linked genres on their own line. Movie heroes use the movie description directly. Continue cards retain the episode still and playback target and identify the child as `Sx Ey`.
- **Extensible architecture:** Config-driven providers, processors, plugins, and a typed Engine/Dashboard boundary make the system practical to extend.

## How It Works

```text
Watch folders
  -> Ingestion
  -> File processors
  -> Identity and scoring
  -> Retail identification (Stage 1)
  -> Wikidata bridge resolution (Stage 2)
  -> Quick hydration
  -> Universe enrichment (Stage 3)
  -> SQLite, .data/assets artwork, organization, write-back
  -> API and SignalR
  -> Dashboard
```

In plain English:

1. A file appears in a configured folder.
2. Tuvima waits until the file is stable, fingerprints it, and reads embedded metadata.
3. Processors extract details from EPUB, audio, video, comic, and other supported files.
4. Stage 1 configured providers provide identity and retail evidence. Music runs MusicBrainz first for canonical music IDs, then Apple for artwork and retail metadata; other lanes use their configured providers such as Apple, TMDB, and Comic Vine.
5. Stage 2 Wikidata resolution uses bridge IDs such as ISBN, TMDB ID, MusicBrainz ID, Apple ID, or Comic Vine ID to find canonical identity when possible. If Stage 1 finds no safe provider match, Wikidata is not used as a broad fallback.
6. Quick Hydration gets the item visible with core identity, managed artwork, and lane shelf assignment. If retail metadata is retained but Wikidata finds no QID, the item can still get a Read, Watch, or Listen shelf without becoming a top-level Collections tile.
7. Managed artwork and headshots are stored under `.data/assets/...` and indexed in SQLite through `entity_assets` or person/entity records. Sidecar files beside media are optional exports only.
8. The Priority Cascade decides which metadata wins, while user corrections stay final.
9. The Dashboard updates through HTTP and SignalR so ingestion and review progress are visible.

Learn more in [How File Ingestion Works](https://tuvima.github.io/tuvima_library/explanation/how-ingestion-works/), [How Enrichment Works](https://tuvima.github.io/tuvima_library/explanation/how-hydration-works/), the [Ingestion, Identity, and Enrichment Pipeline](https://tuvima.github.io/tuvima_library/architecture/ingestion-identity-enrichment-pipeline/), and the [Technical Overview](https://tuvima.github.io/tuvima_library/architecture/technical-overview/).

## Built Today

Tuvima is Early Access, but it is not just a mockup. Current builds include:

- Engine and Dashboard apps for local development.
- Home, Read, Watch, Listen, Collections, Search, detail pages, Settings/Admin, and Review Queue surfaces. Home is the only cinematic landing; Read, Watch, and Listen use compact discovery shelves plus one-click scoped browse routes, and Collections opens directly as a filterable grid.
- A profile-aware navbar with My List, clear account/settings/help actions, permission-gated Needs Review attention, and one circular activity indicator for playback, ingestion, AI, enrichment, and other active Engine work.
- Folder scanning, ingestion operations, file fingerprinting, duplicate handling, review creation, and live progress.
- SQLite persistence with startup schema initialization, `guid-blob-v1` internal GUID storage, and reset/reingest safety for legacy database epochs.
- Relationship-scoped series manifests keep the main sequence separate from short fiction, collected content, and broader franchise context while preserving provider/Wikidata decimal ordinals exactly.
- Provider-neutral sequence manifests retain missing TMDB/Wikidata members, link ownership through stable external IDs, and only show finite completion totals when every position can be represented.
- Series details use consistent Series Set/Series selectors, ownership counts, a Jump to selector for groups over five entries, and carousel arrows adjacent to the visible cards. Canonical series containers surface the same numbered rail on Overview; connectors remain behind the numbered nodes and a stronger purple frame glow identifies the current item without `This book`, `This movie`, or `Up next` labels. **Show missing** inherits a media default from `config/ui/library-preferences.json`, while SQLite stores only explicit profile-and-series overrides.
- EPUB reading routes, video/audio streaming routes, playback/reader progress APIs, and personal playback preferences.
- Inline media editing through the shared editor in normal, review, and batch modes.
- Provider configuration, provider health/status, pipeline priority settings, and config-driven provider adapters.
- Local AI model inventory, downloads, load/unload actions, hardware/resource status, feature flags, vocabulary, and schedules where Engine endpoints exist.
- Plugin listing, enable/disable, settings JSON, dynamic manifests, health checks, and job views for current plugin capabilities.
- API key/profile management, UI/device/profile settings, activity logs, ingestion dashboards, and review workflows.
- Current browsing lives on Home, Read, Watch, Listen, Collections, Search, and detail pages; Settings/Admin handles configuration and operational review.

For the detailed truth table, see [Product Status](https://tuvima.github.io/tuvima_library/product/status/) and [Feature Truth Inventory](https://tuvima.github.io/tuvima_library/product/feature-truth-inventory/).

## Still Outstanding

These areas are intentionally documented as partial, planned, or not connected where appropriate:

- A fully guided first-run wizard.
- Advanced delivery controls, direct-play policy, subtitle/audio delivery preferences, and richer offline-download automation.
- Deeper playlist editing, recommendation generation, and smart collection automation.
- Plugin marketplace install/update flows.
- Some Local AI job controls and per-feature runtime integrations where no public Engine endpoint exists yet.
- Complete multi-user/remote-access hardening beyond the current local-first profile and API-key model.
- OPDS, Audiobookshelf-compatible APIs, import wizards, webhooks, PWA support, and other target-state interoperability work.

The docs mark future-state material explicitly so users can tell what is built from what is planned.

## Supported Media

| Lane | Media | Common formats |
|---|---|---|
| Read | Books, comics | EPUB, PDF, CBZ, CBR |
| Watch | Movies, TV | MKV, MP4, M4V, WEBM, AVI |
| Listen | Music, audiobooks | FLAC, MP3, AAC, M4A, OGG, WAV, M4B |

See [Supported Media Types and Formats](https://tuvima.github.io/tuvima_library/reference/media-types/) for processor and provider details.

## Privacy

Tuvima is designed around local ownership:

- The Engine, Dashboard, database, and local AI run on your machine.
- No Tuvima account is required.
- There is no built-in telemetry or tracking.
- AI inference is local. Model download URLs retrieve model files only.
- External metadata providers are contacted only when configured and needed for enrichment.

Read the full [Privacy and Local-First Behavior](https://tuvima.github.io/tuvima_library/explanation/privacy-local-first/) page.

## Getting Started

Prerequisites:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- About 10 GB free space if you plan to use local AI models
- Optional provider keys for services that require credentials

```bash
git clone https://github.com/Tuvima/tuvima_library.git
cd tuvima_library
dotnet restore MediaEngine.slnx
```

Start the Engine and Dashboard in separate terminals:

```bash
dotnet run --project src/MediaEngine.Api
dotnet run --project src/MediaEngine.Web
```

Default local URLs:

- Engine: `http://localhost:61495`
- Dashboard: `http://localhost:5016`

Open the Dashboard, configure folders in **Settings > Libraries**, confirm providers in **Settings > Providers**, then start a scan from Libraries and watch progress in **Settings > Ingestion**.

Full walkthroughs:

- [Getting Started](https://tuvima.github.io/tuvima_library/tutorials/getting-started/)
- [Your First Library](https://tuvima.github.io/tuvima_library/tutorials/first-library/)
- [How to Add Media](https://tuvima.github.io/tuvima_library/guides/adding-media/)
- [Troubleshooting](https://tuvima.github.io/tuvima_library/guides/troubleshooting/)

## Documentation

Full documentation lives at [tuvima.github.io/tuvima_library](https://tuvima.github.io/tuvima_library/).

| Need | Start here |
|---|---|
| Install and run | [Getting Started](https://tuvima.github.io/tuvima_library/tutorials/getting-started/) |
| Add your first files | [Your First Library](https://tuvima.github.io/tuvima_library/tutorials/first-library/) |
| Configure metadata sources | [Configure Providers](https://tuvima.github.io/tuvima_library/guides/configuring-providers/) |
| Understand product readiness | [Product Status](https://tuvima.github.io/tuvima_library/product/status/) |
| Extend or debug the system | [Technical Overview](https://tuvima.github.io/tuvima_library/architecture/technical-overview/) |
| Look up API routes | [Engine API Reference](https://tuvima.github.io/tuvima_library/reference/api-endpoints/) |
| Review dependencies and data sources | [Attributions](https://tuvima.github.io/tuvima_library/reference/attributions/) |

Preview docs locally:

```powershell
./scripts/docs/build-docs.ps1
./scripts/docs/serve-docs.ps1
```

## Attributions

Tuvima stands on a large open-source and public-knowledge foundation, including .NET, ASP.NET Core, Blazor, MudBlazor, SQLite, Dapper, SignalR, Serilog, Polly, Swashbuckle, TagLibSharp, VersOne.Epub, SkiaSharp, Xabe.FFmpeg, MediaInfo, SharpCompress, LLamaSharp, Whisper.net, Cronos, xUnit, MkDocs, Material for MkDocs, FFmpeg, Wikimedia Commons, Wikipedia, Wikidata, Tuvima.Wikidata, MusicBrainz, TMDB, Open Library, Comic Vine, Fanart.tv, LRCLIB, OpenSubtitles, and Apple APIs.

See [Attributions](https://tuvima.github.io/tuvima_library/reference/attributions/) for the maintained acknowledgement list and notes about optional provider credentials.

## License

Tuvima Library is free and open-source software under the **GNU Affero General Public License v3.0 (AGPLv3)**.

No premium tier. No cloud account requirement. No feature gates.

---

<div align="center">

**You already own the stories. Tuvima makes them easier to find, understand, and enjoy.**

[Documentation](https://tuvima.github.io/tuvima_library/) | [Report a Bug](https://github.com/Tuvima/tuvima_library/issues) | [Request a Feature](https://github.com/Tuvima/tuvima_library/issues)

</div>
