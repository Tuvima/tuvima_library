<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/images/tuvima-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/images/tuvima-logo.svg">
  <img src="assets/images/tuvima-logo.svg" alt="Tuvima Library" height="90" />
</picture>

**Make your media collection discoverable.**

*Tuvima Library is the first media platform that organizes by story, not by file type â€” unifying your books, audiobooks, movies, TV shows, music, and comics into one intelligent library, with a cinematic dashboard and a built-in AI that runs entirely on your machine.*

<br/>

[![License: AGPLv3](https://img.shields.io/badge/License-AGPLv3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20macOS%20%7C%20Windows-lightgrey.svg)]()

</div>

---

## Why Tuvima?

Plex organizes your videos. Calibre organizes your books. Audiobookshelf organizes your audiobooks. But none of them talk to each other. Your Dune ebook lives in one app, the audiobook in another, and the Villeneuve film in a third. Three apps, three databases, three interfaces â€” for the same story.

**Tuvima Library organizes by story, not by file type.**

Drop your files into a folder. Tuvima reads each one, figures out what it is using a built-in AI and Wikidata (the knowledge database behind Wikipedia), downloads cover art and author photos, and files everything into a clean, organized library. Books, audiobooks, movies, TV shows, music, and comics â€” all in one place.

### One story, every format

When Tuvima discovers that your Dune ebook, Dune audiobook, and Dune film belong to the same creative world, it groups them together into a single entry called a **Series**. Click it, and you see every version of the story you own â€” read, listen, or watch. No switching apps.

Related Series are grouped into **Universes** â€” the Dune novels, the Dune films, and the Dune audiobooks all live under the "Dune" Universe. [Learn more about Universes and Series.](https://tuvima.github.io/tuvima_library/explanation/how-universes-work/)

### How Tuvima compares

| | Plex | Jellyfin | Calibre | Audiobookshelf | **Tuvima** |
|---|---|---|---|---|---|
| Video | Yes | Yes | No | No | **Yes** |
| Books | No | No | Yes | No | **Yes** |
| Audiobooks | No | No | Partial | Yes | **Yes** |
| Music | Yes | Yes | No | No | **Yes** |
| Comics | No | No | Partial | No | **Yes** |
| Cross-media linking | No | No | No | No | **Yes** |
| Built-in AI | No | No | No | No | **Yes** |
| Fully local (no cloud) | No | Yes | Yes | Yes | **Yes** |
| Open source | No | Yes | Yes | Yes | **Yes** |

---

## How It Works

Drop a file into a watched folder. The Engine reads it, cleans the filename with AI, searches Wikidata and retail providers for a match, downloads cover art and metadata, enriches the file with themes, mood, and a TL;DR summary â€” then organizes it into your library. Every 30 days it re-checks for updated information.

[Full walkthrough: How Ingestion Works](https://tuvima.github.io/tuvima_library/explanation/how-ingestion-works/) Â· [How Enrichment Works](https://tuvima.github.io/tuvima_library/explanation/how-hydration-works/) Â· [How Scoring Works](https://tuvima.github.io/tuvima_library/explanation/how-scoring-works/)

### Built-in AI

Local AI support is part of the product direction, and current builds can report hardware/resource status where the Engine exposes it. Model management, feature toggles, and runtime limits are still being connected; the Dashboard labels those controls as partial or not connected instead of presenting them as live configuration.

[Learn more about the AI.](https://tuvima.github.io/tuvima_library/explanation/how-ai-works/)

### Supported media

| Type | Formats |
|---|---|
| **Books** | EPUB, PDF |
| **Audiobooks** | M4B, MP3 |
| **Movies** | MKV, MP4, AVI |
| **TV Shows** | MKV, MP4 |
| **Music** | FLAC, MP3, AAC, OGG, WAV |
| **Comics** | CBZ, CBR |

[Full format details, processors, and providers.](https://tuvima.github.io/tuvima_library/reference/media-types/)

---
### Dashboard model

The Dashboard is organized around the way people use their media:

- **Home** is discovery and overview.
- **Read**, **Watch**, and **Listen** are the main media lanes.
- **Search** finds media across the whole library.
- **Detail pages** are where a user views an item and fixes its metadata inline.
- **Review Queue** is only for blocked or uncertain items that need human confirmation.
- **Settings/Admin** is for folders, providers, profiles, ingestion status, system health, and configuration.
- **Library Operations** (`/settings/ingestion`) is the admin view for active ingestion, recent batches, source folder health, provider health, pipeline counts, organization rules, and grouped review reasons.

The old Library Vault workspace has been removed. Normal media corrections now happen inline from the page, row, card, album, track, movie, show, book, comic, or detail view where the issue appears.
When Tuvima is uncertain, it sends only that exception to the Review Queue instead of making the whole library feel like an admin workspace.
---

## Privacy

- **Everything runs locally** â€” your data store, your AI models, your files. Nothing leaves your machine.
- **No accounts** â€” no sign-up, no login, no telemetry, no tracking.
- **AI runs on your hardware** â€” no OpenAI, no Anthropic, no cloud AI calls.
- **Portable data** â€” metadata is written back into your files, so you can take your library anywhere.

---

## Getting Started

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and 10 GB free disk space.

```bash
git clone https://github.com/Tuvima/tuvima_library.git
cd tuvima-library
# config/ is already in the repo — just add your API keys
# Create secret files for any providers that require API keys
# e.g. config/secrets/tmdb.json with {"api_key": "your-key"}
```

Edit `config/core.json` to set your paths, then:

```bash
dotnet run --project src/MediaEngine.Api    # Engine at localhost:61495
dotnet run --project src/MediaEngine.Web    # Dashboard at localhost:5016
```

On first startup the Engine initializes local configuration and begins watching your folders. Local AI readiness is shown as optional status; Phase 0 intentionally labels incomplete AI model and runtime controls instead of treating them as live actions.

For a first library, open the Dashboard and use **Settings > Setup**. The checklist confirms the Engine connection, Library Root, Watch Folder, provider readiness, optional Local AI status, scan state, and pending Review Queue work. Configure folders first, run **Scan Now**, then open **Library Operations** to watch ingestion progress and resolve any uncertain items in the Review Queue.

For a concise product-truth baseline, see [`docs/product/feature-truth-inventory.md`](docs/product/feature-truth-inventory.md).

[Full setup guide](https://tuvima.github.io/tuvima_library/tutorials/getting-started/) Â· [Docker instructions](https://tuvima.github.io/tuvima_library/tutorials/getting-started/#docker) Â· [Configuration reference](https://tuvima.github.io/tuvima_library/reference/configuration/)

---

## Documentation

Full documentation lives on GitHub Pages at [tuvima.github.io/tuvima_library](https://tuvima.github.io/tuvima_library/), organised using the [DiÃ¡taxis framework](https://diataxis.fr/):

| | For users | For developers |
|---|---|---|
| **Tutorials** | [Getting Started](https://tuvima.github.io/tuvima_library/tutorials/getting-started/), [Your First Library](https://tuvima.github.io/tuvima_library/tutorials/first-library/) | [Developer Setup](https://tuvima.github.io/tuvima_library/tutorials/dev-setup/) |
| **How-to Guides** | [Adding Media](https://tuvima.github.io/tuvima_library/guides/adding-media/), [Resolving Reviews](https://tuvima.github.io/tuvima_library/guides/resolving-reviews/), [Providers](https://tuvima.github.io/tuvima_library/guides/configuring-providers/), [Languages](https://tuvima.github.io/tuvima_library/guides/language-setup/) | [Adding a Provider](https://tuvima.github.io/tuvima_library/guides/adding-a-provider/), [Writing a Processor](https://tuvima.github.io/tuvima_library/guides/writing-a-processor/), [Running Tests](https://tuvima.github.io/tuvima_library/guides/running-tests/) |
| **Reference** | [Configuration](https://tuvima.github.io/tuvima_library/reference/configuration/), [Media Types](https://tuvima.github.io/tuvima_library/reference/media-types/), [Glossary](https://tuvima.github.io/tuvima_library/reference/glossary/) | [API Endpoints](https://tuvima.github.io/tuvima_library/reference/api-endpoints/), [Database Schema](https://tuvima.github.io/tuvima_library/reference/database-schema/) |
| **Explanation** | [Ingestion](https://tuvima.github.io/tuvima_library/explanation/how-ingestion-works/), [Scoring](https://tuvima.github.io/tuvima_library/explanation/how-scoring-works/), [Universes](https://tuvima.github.io/tuvima_library/explanation/how-universes-work/), [AI](https://tuvima.github.io/tuvima_library/explanation/how-ai-works/), [Enrichment](https://tuvima.github.io/tuvima_library/explanation/how-hydration-works/) | [Architecture deep-dives](https://tuvima.github.io/tuvima_library/architecture/ingestion-pipeline/) |

Published documentation is available on GitHub Pages at [tuvima.github.io/tuvima_library](https://tuvima.github.io/tuvima_library/).

Docs can also be previewed locally with:

```powershell
./scripts/docs/build-docs.ps1
./scripts/docs/serve-docs.ps1
```

---

## License

Tuvima Library is free and open-source software, licensed under the **GNU Affero General Public License v3.0 (AGPLv3)**.

No premium tiers. No feature gates. No "Pro" version.

---

<div align="center">

**You already own the stories. Tuvima makes them discoverable.**

No subscriptions. No cloud. No compromises. Just your stories, finally together.

[Report a Bug](https://github.com/Tuvima/tuvima_library/issues) · [Request a Feature](https://github.com/Tuvima/tuvima_library/issues)

</div>

