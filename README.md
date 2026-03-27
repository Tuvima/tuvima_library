<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/images/tuvima-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/images/tuvima-logo.svg">
  <img src="assets/images/tuvima-logo.svg" alt="Tuvima Library" height="90" />
</picture>

**Make your media collection discoverable.**

*Tuvima Library is the first media platform that organizes by story, not by file type — unifying your books, audiobooks, movies, TV shows, music, comics, and podcasts into one intelligent library, with a cinematic dashboard and a built-in AI that runs entirely on your machine.*

<br/>

[![License: AGPLv3](https://img.shields.io/badge/License-AGPLv3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20macOS%20%7C%20Windows-lightgrey.svg)]()

</div>

---

## Why Tuvima?

Plex organizes your videos. Calibre organizes your books. Audiobookshelf organizes your audiobooks. But none of them talk to each other. Your Dune ebook lives in one app, the audiobook in another, and the Villeneuve film in a third. Three apps, three databases, three interfaces — for the same story.

**Tuvima Library organizes by story, not by file type.**

Drop your files into a folder. Tuvima reads each one, figures out what it is using a built-in AI and Wikidata (the knowledge database behind Wikipedia), downloads cover art and author photos, and files everything into a clean, organized library. Books, audiobooks, movies, TV shows, music, comics, and podcasts — all in one place.

### One story, every format

When Tuvima discovers that your Dune ebook, Dune audiobook, and Dune film belong to the same creative world, it groups them together into a single entry called a **Series**. Click it, and you see every version of the story you own — read, listen, or watch. No switching apps.

Related Series are grouped into **Universes** — the Dune novels, the Dune films, and the Dune audiobooks all live under the "Dune" Universe.

### How Tuvima compares

| | Plex | Jellyfin | Calibre | Audiobookshelf | **Tuvima** |
|---|---|---|---|---|---|
| Video | Yes | Yes | No | No | **Yes** |
| Books | No | No | Yes | No | **Yes** |
| Audiobooks | No | No | Partial | Yes | **Yes** |
| Music | Yes | Yes | No | No | **Yes** |
| Comics | No | No | Partial | No | **Yes** |
| Podcasts | No | No | No | No | **Yes** |
| Cross-media linking | No | No | No | No | **Yes** |
| Built-in AI | No | No | No | No | **Yes** |
| Fully local (no cloud) | No | Yes | Yes | Yes | **Yes** |
| Open source | No | Yes | Yes | Yes | **Yes** |

---

## What Happens When You Add a File

Here's the journey every file takes, from the moment you drop it into the watch folder:

**1. The AI reads the filename**
A messy filename like `[HD] Frank Herbert - Dune (1965) [x264].epub` gets cleaned up automatically. The AI extracts the title ("Dune"), the author ("Frank Herbert"), and the year (1965). This works even with special characters, foreign languages, and terrible naming.

**2. The Engine identifies the file**
Using the cleaned-up information, the Engine searches Wikidata and retail providers (Apple, Google Books, Open Library, TMDB) to find the exact match. It downloads cover art, descriptions, ratings, and bridge identifiers (ISBN, TMDB ID). If the file already has cover art embedded, the Engine compares it visually against provider images to improve matching accuracy.

**3. Wikidata confirms the identity**
Wikidata is the authority for structured data — title, author, year, genre, series. The Engine fetches 50+ properties and builds a complete profile. Wikipedia plot summaries are downloaded for AI enrichment. Author headshots, biographies, and social links are fetched automatically.

**4. The AI enriches your library**
In the background, the AI reads the descriptions and plot summaries, then extracts structured vocabulary for every file:
- **Themes** — what the story is about (ecology, friendship, duality)
- **Mood** — how it feels (atmospheric, cerebral, haunting, cozy)
- **Setting** — where it takes place (Middle-earth, 1960s Tokyo, post-apocalyptic United States)
- **Pace** — how it reads (slow-burn, fast-paced, meditative)
- **TL;DR** — one punchy sentence that captures the essence
- **Characters** — names and roles extracted from descriptions

**5. The file is organized**
Once the Engine is confident in the identification, the file moves from a staging area into your organized library — sorted by media type, named by title, with cover art and hero banners alongside it.

**6. Everything stays up to date**
Every 30 days, the Engine re-checks Wikidata and retail providers for updated information. New cover art, corrected metadata, additional translations — your library gets better over time without any effort.

---

## The Dashboard

A cinematic dark-mode interface designed to look like Netflix or Apple TV+, but running entirely on your machine.

- **Poster swimlanes** — horizontal-scrolling rows of cover art, just like a streaming service
- **Hero banners** — blurred backdrop images with film-grain texture for each story
- **Real-time updates** — new files appear the moment they're detected, no refresh needed
- **Works on any screen** — desktop, tablet, mobile, or TV
- **Global search** (`Ctrl+K`) — find anything in your library instantly

### Library Vault

The command centre for managing everything. Four tabs:

- **Media** — every file in your library with status indicators, pipeline progress, and a detail drawer for each item
- **People** — every author, narrator, and director with Wikidata headshots and biographies
- **Universes** — franchise-level groupings that link related Series together
- **Hubs** — smart collections, personal lists, AI-generated mixes, and user playlists

### Settings

The AI section in Settings shows you exactly what's happening:

- **Models** — which AI models are downloaded, loaded, and how much memory they use. Hardware profile badge showing your detected tier, GPU status, and inference speed.
- **Features** — toggle individual AI capabilities on or off. Each shows which model it uses and when it runs.
- **Schedule** — when background enrichment runs, with a progress bar showing how much of your library has been enriched.

---

## Built-In AI

Every AI feature runs entirely on your machine. No cloud account, no subscription, no data ever leaves your home. Models download automatically on first startup (~9 GB total).

### Four models, four jobs

| Model | Size | Job | What it does |
|-------|------|-----|-------------|
| **Llama 3.2 1B** | 750 MB | Quick tasks | Classifies media types (is this MP3 music or an audiobook?), parses search queries |
| **Llama 3.2 3B** | 1.9 GB | Ingestion | Cleans filenames, picks the right Wikidata match, generates vibe tags |
| **Llama 3.1 8B** | 4.6 GB | Deep enrichment | Extracts themes, mood, setting, pace, TL;DR, and character names from descriptions |
| **Whisper Medium** | 1.5 GB | Audio | Transcribes audiobooks, detects spoken language, syncs subtitles |

### Every feature, on every machine

The Engine benchmarks your hardware on first startup and adapts automatically. If you have a gaming PC with a dedicated GPU, enrichment runs continuously and finishes in under an hour. If you have a quieter machine, it schedules the heavy work for overnight when you're not using it.

**Every machine gets every feature.** The only difference is speed:

| | Low (i5, 8 GB, no GPU) | Medium (i7, 16 GB) | High (RTX 3060+) |
|---|---|---|---|
| Files appear in your library | ~2 hours | ~1.5 hours | ~1.3 hours |
| Themes, mood, and TL;DR | ~3 nights | ~8 hours | ~1 hour |
| Audiobook transcription | ~4-5 nights | ~18 hours | ~5 hours |

> Ingestion speed is partly limited by Wikidata's rate limit (~1 request per second), which is the same regardless of your hardware.

### GPU support

If you have a dedicated GPU, the Engine detects it automatically:

- **NVIDIA** (RTX/GTX) — detected via CUDA, 6-7x faster inference
- **AMD** (Radeon) — detected via Vulkan
- **Intel Arc** — detected via Vulkan

Integrated GPUs (Intel UHD, Intel Iris) are left alone for video transcoding — the AI runs on the CPU instead, so they don't compete for resources.

### Plays nice with transcoding

If FFmpeg or HandBrake is running a video encode, the Engine detects it and backs off — your transcode gets full priority. AI enrichment resumes when the system is idle again.

---

## System Requirements

### Minimum

| | Requirement |
|---|---|
| **CPU** | 4-core (Intel i5 / Ryzen 5 or equivalent) |
| **RAM** | 8 GB |
| **Disk** | 10 GB free for AI models and database |
| **OS** | Windows 10+, Linux, or macOS |
| **GPU** | Not required |

### Recommended

| | Requirement |
|---|---|
| **CPU** | 6+ core (Intel i7 / Ryzen 7) |
| **RAM** | 16 GB |
| **Disk** | SSD with 10 GB free |
| **GPU** | Any dedicated NVIDIA, AMD, or Intel Arc GPU |

---

## How Metadata Works

### The Priority Cascade

When multiple sources disagree about a piece of information (the book title, the author's name, the release year), the Engine resolves the conflict automatically using a priority system:

1. **Your personal data wins first** — your ratings, media type corrections, and custom tags are always preserved
2. **Configured priorities** — you can tell the Engine to prefer a specific source for specific fields (e.g., always use Apple for cover art)
3. **Wikidata is the authority** — for factual data like title, author, year, and genre, Wikidata always wins
4. **Highest confidence wins** — when no authority exists, the source with the best confidence score wins

Factual fields like title, author, year, and genre cannot be manually overridden — they come from authoritative sources only. This keeps your library accurate even as you grow it.

### Two-stage enrichment

After the AI cleans the filename and the file is identified, the Engine enriches it in two background stages:

1. **Retail providers first** — Apple, Google Books, Open Library, and TMDB provide cover art, descriptions, ratings, and bridge identifiers (ISBN, ASIN, TMDB ID). Cover art is visually compared to the file's embedded artwork using perceptual hashing.

2. **Wikidata second** — using the bridge identifiers from retail providers, Wikidata performs a precise lookup and fetches 50+ properties (author, genre, series, year, franchise). Wikipedia plot summaries are downloaded for AI enrichment.

Authors, narrators, and directors are enriched automatically in parallel — headshots, biographies, occupation, and social media links, all from Wikidata.

### Supported metadata providers

**No API key needed:**

| Provider | Media types | What it provides |
|---|---|---|
| Wikidata | Everything | The primary identity source — 50+ properties per item, franchise/series relationships, person data |
| Wikipedia | Everything | Descriptions and plot summaries for AI enrichment |
| Apple API | Books, Audiobooks, Podcasts | High-quality cover art, descriptions, ratings |
| Open Library | Books | Title, author, year, cover art, ISBN, series |
| Google Books | Books | Title, author, year, cover art, description |
| MusicBrainz | Music | Artist, album, year, genre |

**Free API key required:**

| Provider | Media types | What it provides |
|---|---|---|
| TMDB | Movies, TV | Posters, backdrop images, cast/crew |
| Comic Vine | Comics | Covers, issue details |
| Podcast Index | Podcasts | Episode lists, GUIDs |

---

## Privacy

Tuvima Library is private by design:

- **Everything runs locally** — your database, your AI models, your files. Nothing is sent to the cloud.
- **No accounts** — no sign-up, no login, no telemetry, no tracking.
- **AI models run on your CPU/GPU** — no OpenAI, no Anthropic, no API calls to AI services.
- **Your data stays portable** — metadata is written back into your files (EPUB tags, ID3 tags), so you can take your library anywhere.
- **API keys are encrypted** — stored using your operating system's secure credential storage.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 10 GB of free disk space (for AI models)

### Installation

```bash
# 1. Clone the repository
git clone https://github.com/shyfaruqi/tuvima-library.git
cd tuvima-library

# 2. Create your local configuration
cp -r config.example config
```

Edit `config/core.json` and set your paths:

```json
{
  "database_path": "/your/path/library.db",
  "data_root":     "/your/media/library"
}
```

```bash
# 3. Start the Engine
dotnet run --project src/MediaEngine.Api

# 4. Start the Dashboard
dotnet run --project src/MediaEngine.Web
```

The Engine runs at `http://localhost:61495`. The Dashboard runs at `http://localhost:5016`.

On first startup, the Engine will:
1. Download AI models (~9 GB, one-time)
2. Benchmark your hardware (10-15 seconds)
3. Start watching your configured folders

### Docker

```bash
docker build -t tuvima-library .
docker run -p 5016:5016 -p 61495:61495 \
  -v /your/library:/data/library \
  --gpus all \
  tuvima-library
```

> Add `--gpus all` if you have an NVIDIA GPU with the [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html) installed.

### Configuration

Settings live in `config/` as individual JSON files:

| File | What it controls |
|---|---|
| `core.json` | Database path, library root, language preferences |
| `ai.json` | AI model paths, feature toggles, enrichment schedule, hardware profile |
| `scoring.json` | Confidence thresholds for auto-linking and review |
| `hydration.json` | Enrichment stage timeouts and concurrency |
| `providers/*.json` | Per-provider settings (endpoints, throttle, cache) |
| `libraries.json` | Watch folder paths and media type mappings |

---

## Supported Media Types

| Type | File formats | What Tuvima does with it |
|---|---|---|
| **Books** | EPUB, PDF | Extracts title, author, series from embedded metadata. In-browser EPUB reader. |
| **Audiobooks** | M4B, MP3 | AI classifies MP3s as audiobook vs music. Whisper transcribes for subtitle sync. |
| **Movies** | MKV, MP4, AVI | TMDB matching for posters, cast/crew, and franchise grouping. |
| **TV Shows** | MKV, MP4 | Season/episode extraction. Series alignment via Wikidata. |
| **Music** | FLAC, MP3, AAC | MusicBrainz matching for artist, album, genre. Audio fingerprinting for similarity. |
| **Comics** | CBZ, CBR, PDF | Comic Vine matching for issue details and cover art. |
| **Podcasts** | MP3, M4A | Podcast Index matching for episode lists and GUIDs. |

---

## License

Tuvima Library is free and open-source software, licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**.

You are free to use, modify, and distribute Tuvima Library. If you deploy a modified version as a network service, you must make your modifications available under the same license.

No premium tiers. No feature gates. No "Pro" version.

---

<div align="center">

**You already own the stories. Tuvima makes them discoverable.**

No subscriptions. No cloud. No compromises. Just your stories, finally together.

[Report a Bug](https://github.com/shyfaruqi/tuvima-library/issues) · [Request a Feature](https://github.com/shyfaruqi/tuvima-library/issues)

</div>
