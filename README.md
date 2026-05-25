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
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-c9922e.svg)](https://tuvima.github.io/tuvima_library/)
[![Status](https://img.shields.io/badge/status-Early%20Access-f0ad4e.svg)](https://tuvima.github.io/tuvima_library/product/status/)

</div>

---

## What Tuvima Is

Tuvima Library is an Early Access media library for people with real collections on disk. It is built for the person whose *Dune* ebook, audiobook, film, soundtrack, and comics should feel like one library, not six disconnected apps.

The Engine runs locally, watches the folders you choose, reads incoming files, identifies what they are, enriches them with metadata and artwork, stores normalized records in SQLite, and serves a Blazor Dashboard for browsing, search, reading, playback, settings, and review.

The product is branded as **Tuvima Library**. The code still uses `MediaEngine.*` project and namespace names in many places; in this repository, those names refer to the same product.

## Why It Matters

- **Story-first organization:** Read, Watch, and Listen stay focused on what you can do next. Collections appear only when a broader world spans multiple shelves.
- **Local-first privacy:** Your files, database, models, and processing stay on your machine. Provider calls are only for metadata lookups that you configure.
- **Honest automation:** Strong matches flow through automatically. Low-confidence, blocked, or uncertain items go to the Review Queue instead of being silently misfiled.
- **One dashboard:** Home, media lanes, Search, detail pages, Settings/Admin, and Review Queue work together instead of forcing every correction into a separate management workspace.
- **Extensible architecture:** Config-driven providers, processors, plugins, and a typed Engine/Dashboard boundary make the system practical to extend.

## How It Works

```text
Watch folders
  -> Ingestion
  -> File processors
  -> Identity and scoring
  -> Retail identification
  -> Wikidata bridge resolution
  -> SQLite, artwork, organization, write-back
  -> API and SignalR
  -> Dashboard
```

In plain English:

1. A file appears in a configured folder.
2. Tuvima waits until the file is stable, fingerprints it, and reads embedded metadata.
3. Processors extract details from EPUB, audio, video, comic, and other supported files.
4. Retail providers such as Apple, TMDB, MusicBrainz, Comic Vine, or Open Library can provide artwork, descriptions, ratings, and bridge IDs.
5. Wikidata resolution uses bridge IDs such as ISBN, TMDB ID, MusicBrainz ID, or Comic Vine ID to find canonical identity when possible.
6. The Priority Cascade decides which metadata wins, while user corrections stay final.
7. The Dashboard updates through HTTP and SignalR so ingestion and review progress are visible.

Learn more in [How File Ingestion Works](https://tuvima.github.io/tuvima_library/explanation/how-ingestion-works/), [How Enrichment Works](https://tuvima.github.io/tuvima_library/explanation/how-hydration-works/), and the [Technical Overview](https://tuvima.github.io/tuvima_library/architecture/technical-overview/).

## Built Today

Tuvima is Early Access, but it is not just a mockup. Current builds include:

- Engine and Dashboard apps for local development.
- Home, Read, Watch, Listen, Collections, Search, detail pages, Settings/Admin, and Review Queue surfaces.
- Folder scanning, ingestion operations, file fingerprinting, duplicate handling, review creation, and live progress.
- SQLite persistence with startup schema initialization and migrations.
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

Open the Dashboard, go to **Settings > Setup**, confirm folders/providers/readiness, then use **Scan Now** and watch progress in **Settings > Ingestion**.

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
