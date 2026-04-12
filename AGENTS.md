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

## Repository Map

### Main application code

- `src/MediaEngine.Api`
  - The main composition root.
  - Registers nearly all services, repositories, background jobs, health checks, SignalR, and API endpoints.
  - If you want to understand what the whole system starts and how parts are wired together, start here.

- `src/MediaEngine.Web`
  - The Blazor Server dashboard.
  - Uses typed HTTP clients to call the Engine API.
  - Uses SignalR for live progress and activity updates.

- `src/MediaEngine.Domain`
  - The shared language of the system.
  - Holds core aggregates, entities, enums, constants, and interfaces.
  - This project defines things like assets, editions, works, collections, universes, profiles, and the contracts other layers implement.

- `src/MediaEngine.Storage`
  - SQLite repositories, embedded schema bootstrap, startup migrations, and config loading.
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
  -> SQLite + file organization + write-back
  -> API + SignalR
  -> Dashboard
```

In plain English:

1. A file appears in a watched folder.
2. The ingestion layer notices it, waits for file activity to settle, and makes sure it is not a duplicate.
3. File processors inspect the file itself and extract whatever metadata is already available.
4. Intelligence services decide the best current title/author/type values from the claims gathered so far.
5. Retail Identification (Stage 1) searches commercial catalogues for cover art, descriptions, ratings, and bridge identifiers (ISBN, ASIN, TMDB ID). If no retail provider matches, the item goes to review — Wikidata is never attempted.
6. Wikidata Bridge Resolution (Stage 2) uses bridge IDs from Stage 1 to find the canonical Wikidata entity (QID). This provides universe linkage, person relationships, and canonical metadata. If Stage 2 finds no QID, the item keeps its retail data and is flagged for periodic re-checking.
7. Storage writes the results into SQLite and caches supporting data for fast reads later.
8. Organization and write-back services can move files into the library structure and write metadata back into the files.
9. The API exposes all of that state to the Dashboard.
10. SignalR broadcasts live events so the UI can update while ingestion and enrichment are happening.

Two-pass enrichment: The Quick pass gets the item visible on the Dashboard fast (core identity + cover art). The Universe pass runs later in the background for deep enrichment (relationships, people, fictional entities, additional images).

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
2. `src/MediaEngine.Api/Program.cs`
3. `src/MediaEngine.Web/Program.cs`
4. `src/MediaEngine.Ingestion/IngestionEngine.cs`
5. `src/MediaEngine.Providers/Services/HydrationPipelineService.cs`
6. `src/MediaEngine.Intelligence/PriorityCascadeEngine.cs`
7. `src/MediaEngine.Storage/ConfigurationDirectoryLoader.cs`
8. `src/MediaEngine.Storage/DatabaseConnection.cs`

That sequence explains the product, the runtime wiring, the intake flow, the enrichment flow, the decision logic, and the persistence/config layer.

## Practical Reading Notes

- Ignore `bin/`, `obj/`, `.data/`, and `logs/` when learning the architecture.
- Expect product branding (`Tuvima`) and code naming (`MediaEngine`) to coexist.
- The Engine is the operational center of the app.
- The Dashboard is mostly a client of the Engine, not the place where core business rules live.
- Tests are organized by layer, so they are often the fastest way to confirm intended behavior once you know which project you are in.
