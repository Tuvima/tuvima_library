# Glossary

Reference definitions for Tuvima Library terms. User-facing names are used throughout the Dashboard and documentation. Internal code names appear in parentheses where both exist.

---

## C

**Canonical Value**
The winning claim for a metadata field after the Priority Cascade has resolved all competing sources. Stored in `canonical_values` / `canonical_value_arrays`. Displayed in the Dashboard and written back to file tags on sync.

**Claim**
A single piece of metadata from a specific source, tagged with a confidence score (0.0–1.0) and a source identifier. Multiple claims for the same field compete in the Priority Cascade. All claims are append-only; history is never lost. Stored in `metadata_claims`.

## D

**Dashboard**
The browser interface (`MediaEngine.Web`) served at `http://localhost:5016`. Displays the library, receives real-time updates from the Engine via SignalR, and communicates with the Engine exclusively via HTTP.

## E

**Edition**
A specific physical or digital version of a Work. Examples: "4K HDR Blu-ray Remux", "Audible Whispersync Edition", "First Edition Hardcover". One Work may have many Editions. An Edition may have many Media Assets (e.g., a multi-disc rip).

**Engine**
The intelligence and data layer (`MediaEngine.Api`) served at `http://localhost:61495`. Handles all file monitoring, metadata processing, enrichment, and database operations. The Dashboard never touches data directly — it asks the Engine.

## H

**Hub** *(internal name; user-facing: Series)*
See **Series**.

**Hydration**
The two-stage enrichment process that runs after a file is ingested. Stage 1 (RetailIdentification) gathers cover art, descriptions, ratings, and bridge IDs from retail providers. Stage 2 (WikidataBridge) uses those bridge IDs for precise QID resolution and fetches canonical properties from Wikidata.

## L

**Library Folder**
A configured directory entry in `config/libraries.json`. Each Library Folder tells the Engine where to look, which media types to expect, and whether to watch for new files continuously or run a one-time import scan.

## M

**Media Asset**
A single file on disk (e.g., `dune.mkv`, `foundation.epub`, `gatsby.m4b`). The lowest level in the hierarchy. Each Media Asset has a SHA-256 fingerprint so the Engine can track it even if renamed or moved.

**Media Type**
The format category assigned to a file. The seven supported types are: Books, Audiobooks, Movies, TV, Music, Comics, and Podcasts. Ambiguous formats (MP3, MP4) are classified using heuristics and AI.

## P

**ParentHub** *(internal name; user-facing: Universe)*
See **Universe**.

**Priority Cascade**
The four-tier system that resolves metadata conflicts when multiple sources disagree. Tiers in order: Tier A (user locks always win) → Tier B (per-field provider priority from config) → Tier C (Wikidata authority) → Tier D (highest confidence wins). Configured in `config/scoring.json`.

**Processor**
Code that opens a specific file format and extracts embedded metadata. One processor per format family: `EpubProcessor`, `AudioProcessor`, `VideoProcessor`, `ComicProcessor`. Processors produce Claims at high confidence (typically 0.85–1.0).

**Provider**
An external service that supplies metadata. Providers are called during Hydration. Examples: Apple API, Open Library, Google Books, TMDB, MusicBrainz, Metron, Wikidata, Fanart.tv. Each provider has a self-contained config file in `config/providers/`.

## Q

**QID**
A Wikidata entity identifier. Format: `Q` followed by digits (e.g., `Q190804` = Dune). QIDs are the Engine's canonical identity anchor — once a Work has a QID, structured metadata (title, author, year, genre) comes exclusively from Wikidata.

## S

**Series** *(internal name: Hub)*
A sub-grouping within a Universe — a specific sequence or collection of related works. Examples: "Dune Novels", "Dune Films". A Series is a flexible container for any creative grouping; it is not limited to numbered sequences. Series are resolved at metadata-scoring time and have no presence on the filesystem.

**Staging**
The `.data/staging/` area where files live between ingestion and promotion. Each in-flight asset gets its own subfolder (`{assetId12}/`). Files with confidence ≥ 0.85 are promoted to the organized library; explicitly rejected files move to `.data/staging/rejected/`.

## U

**Universe** *(internal name: ParentHub)*
A franchise-level grouping that links multiple Series sharing the same creative world. Examples: "Dune", "Marvel", "Tolkien". Universes are optional — a Series without a parent franchise sits directly under the Library. Universes are always Wikidata-sourced and auto-cleaned when all child Series are removed.

## V

**Vault**
The Library Vault at `/vault`. The command centre for managing everything in the library. Contains four tabs: Media, People, Universes, and Hubs. Provides batch operations, detail drawers, pipeline visibility, and inline resolution for items needing review.

**Vibe Tags**
AI-generated mood and atmosphere descriptors, distinct from genre. Genres (from Wikidata and retail providers) describe *what something is*. Vibes describe *how it feels*. Each media type has 25–30 vibe vocabulary entries across four dimensions: theme, mood, setting, and pace. Used by Intent Search for natural-language discovery.

## W

**Work**
A single title — one creative work, independent of format or version. Examples: "Dune Part One", "The Godfather". One Work may have many Editions. Works are deduplicated by title + author + media type so that duplicate files create new Editions rather than duplicate Works.

**Writeback**
The process of writing resolved metadata back into file tags after enrichment or user correction. Formats: EPUB OPF metadata, ID3 tags (MP3/M4B), MP4 atoms. Controlled by `config/writeback.json`. Enabled by default.
