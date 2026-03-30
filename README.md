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

Related Series are grouped into **Universes** — the Dune novels, the Dune films, and the Dune audiobooks all live under the "Dune" Universe. [Learn more about Universes and Series.](docs/explanation/how-universes-work.md)

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

## How It Works

Drop a file into a watched folder. The Engine reads it, cleans the filename with AI, searches Wikidata and retail providers for a match, downloads cover art and metadata, enriches the file with themes, mood, and a TL;DR summary — then organizes it into your library. Every 30 days it re-checks for updated information.

[Full walkthrough: How Ingestion Works](docs/explanation/how-ingestion-works.md) · [How Enrichment Works](docs/explanation/how-hydration-works.md) · [How Scoring Works](docs/explanation/how-scoring-works.md)

### Built-in AI

Four local AI models (~9 GB total) handle filename parsing, media classification, vibe tagging, and audiobook transcription. Everything runs on your CPU/GPU — no cloud, no subscription. The Engine benchmarks your hardware on first startup and adapts: gaming PCs run enrichment continuously, quieter machines schedule it overnight.

[Learn more about the AI.](docs/explanation/how-ai-works.md)

### Supported media

| Type | Formats |
|---|---|
| **Books** | EPUB, PDF |
| **Audiobooks** | M4B, MP3 |
| **Movies** | MKV, MP4, AVI |
| **TV Shows** | MKV, MP4 |
| **Music** | FLAC, MP3, AAC, OGG, WAV |
| **Comics** | CBZ, CBR |
| **Podcasts** | MP3, M4A |

[Full format details, processors, and providers.](docs/reference/media-types.md)

---

## Privacy

- **Everything runs locally** — your data store, your AI models, your files. Nothing leaves your machine.
- **No accounts** — no sign-up, no login, no telemetry, no tracking.
- **AI runs on your hardware** — no OpenAI, no Anthropic, no cloud AI calls.
- **Portable data** — metadata is written back into your files, so you can take your library anywhere.

---

## Getting Started

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and 10 GB free disk space.

```bash
git clone https://github.com/shyfaruqi/tuvima-library.git
cd tuvima-library
cp -r config.example config
```

Edit `config/core.json` to set your paths, then:

```bash
dotnet run --project src/MediaEngine.Api    # Engine at localhost:61495
dotnet run --project src/MediaEngine.Web    # Dashboard at localhost:5016
```

On first startup the Engine downloads AI models (~9 GB), benchmarks your hardware, and begins watching your folders.

[Full setup guide](docs/tutorials/getting-started.md) · [Docker instructions](docs/tutorials/getting-started.md#docker) · [Configuration reference](docs/reference/configuration.md)

---

## Documentation

Full documentation lives in [`docs/`](docs/index.md), organised using the [Diátaxis framework](https://diataxis.fr/):

| | For users | For developers |
|---|---|---|
| **Tutorials** | [Getting Started](docs/tutorials/getting-started.md), [Your First Library](docs/tutorials/first-library.md) | [Developer Setup](docs/tutorials/dev-setup.md) |
| **How-to Guides** | [Adding Media](docs/guides/adding-media.md), [Resolving Reviews](docs/guides/resolving-reviews.md), [Providers](docs/guides/configuring-providers.md), [Languages](docs/guides/language-setup.md) | [Adding a Provider](docs/guides/adding-a-provider.md), [Writing a Processor](docs/guides/writing-a-processor.md), [Running Tests](docs/guides/running-tests.md) |
| **Reference** | [Configuration](docs/reference/configuration.md), [Media Types](docs/reference/media-types.md), [Glossary](docs/reference/glossary.md) | [API Endpoints](docs/reference/api-endpoints.md), [Database Schema](docs/reference/database-schema.md) |
| **Explanation** | [Ingestion](docs/explanation/how-ingestion-works.md), [Scoring](docs/explanation/how-scoring-works.md), [Universes](docs/explanation/how-universes-work.md), [AI](docs/explanation/how-ai-works.md), [Enrichment](docs/explanation/how-hydration-works.md), [Vault](docs/explanation/how-the-vault-works.md) | [Architecture deep-dives](docs/architecture/) |

---

## License

Tuvima Library is free and open-source software, licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**.

No premium tiers. No feature gates. No "Pro" version.

---

<div align="center">

**You already own the stories. Tuvima makes them discoverable.**

No subscriptions. No cloud. No compromises. Just your stories, finally together.

[Report a Bug](https://github.com/shyfaruqi/tuvima-library/issues) · [Request a Feature](https://github.com/shyfaruqi/tuvima-library/issues)

</div>
