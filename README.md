<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/images/tuvima-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset="assets/images/tuvima-logo.svg">
  <img src="assets/images/tuvima-logo.svg" alt="Tuvima Library" height="90" />
</picture>

**Your books, films, shows, music, audiobooks, and comics—together in one private library.**

Tuvima Library turns the media you already own into a collection that is easier to explore, understand, and enjoy.

[Get Started](https://tuvima.github.io/tuvima_library/tutorials/getting-started/) ·
[Read the Documentation](https://tuvima.github.io/tuvima_library/) ·
[See Product Status](https://tuvima.github.io/tuvima_library/product/status/)

<br/>

[![License: AGPLv3](https://img.shields.io/badge/License-AGPLv3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Status](https://img.shields.io/badge/status-Early%20Access-f0ad4e.svg)](https://tuvima.github.io/tuvima_library/product/status/)

</div>

---

## Your Collection Should Feel Like a Library

Personal media collections rarely live in one neat place. A single story might be an ebook in one folder, an audiobook in another, a film on a hard drive, and a soundtrack mixed into a music collection. Traditional media servers tend to separate those files by format.

Tuvima Library starts with the story.

Point Tuvima at the folders you choose and it builds a rich, browsable library around them. It identifies each item, adds useful metadata and artwork, connects related works where it has trustworthy evidence, and remembers your progress. Instead of searching through folders and filenames, you can discover what you own and decide whether you want to read, watch, or listen.

## From Files to a Living Library

### Bring the collection you already have

Keep your existing books, comics, movies, TV episodes, music, and audiobooks on your own disks. Tuvima watches the folders you configure and notices when media is added or changed.

### Let Tuvima organize the details

Tuvima reads the files, identifies their contents, and enriches them with titles, descriptions, credits, artwork, series information, and other useful context. Confident matches flow into the library automatically; uncertain items wait in a Review Queue for you.

### Find something worth returning to

Home helps you continue where you stopped and rediscover the collection. Read, Watch, and Listen give each kind of experience a natural home, while search reaches across the whole library. Series, creators, and broader collections help reveal connections that folders alone cannot.

### Enjoy it without giving up control

Read supported books, play audio and video, track progress, and correct an item from its own page. Your library remains yours: the catalog, artwork, progress, and optional AI processing stay on your machine.

## Built Around Ownership and Privacy

- **Local first:** the Engine, Dashboard, SQLite database, managed artwork, and optional AI models run locally.
- **No Tuvima account:** using your library does not require a hosted account or subscription.
- **No built-in tracking:** Tuvima does not include product telemetry.
- **Your choice of metadata sources:** external providers are contacted only when configured and needed.
- **Human review when it matters:** low-confidence matches are surfaced instead of silently treated as correct.
- **Free and open source:** there is no premium tier or feature gate.

Read more about [Privacy and Local-First Behavior](https://tuvima.github.io/tuvima_library/explanation/privacy-local-first/).

## What You Can Add

| Experience | Media | Common formats |
|---|---|---|
| Read | Books and comics | EPUB, PDF, CBZ, CBR |
| Watch | Movies and TV | MKV, MP4, M4V, WEBM, AVI |
| Listen | Music and audiobooks | FLAC, MP3, AAC, M4A, OGG, WAV, M4B |

See [Supported Media Types and Formats](https://tuvima.github.io/tuvima_library/reference/media-types/) for the complete, current list.

## Early Access

Tuvima Library is under active development. The core Engine and Dashboard are real and usable today, but some experiences are still being refined.

Current builds include:

- Folder scanning, file identification, metadata enrichment, artwork management, and duplicate handling.
- Home, Read, Watch, Listen, Collections, library-wide Search, and rich detail pages.
- EPUB reading plus audio and video playback with saved progress and personal preferences.
- Series, people, playlists, and collection views backed by library data.
- Inline corrections and a Review Queue for items that need help.
- Settings for libraries, providers, profiles, local AI, plugins, ingestion, and system health.

Features still in development include:

- A guided first-run experience.
- Richer recommendations, playlists, and smart collections.
- More advanced playback, subtitles, delivery, and offline controls.
- Plugin marketplace installation and updates.
- Broader remote-access hardening and interoperability.
- Deeper integration of local AI across library workflows.

The [Product Status](https://tuvima.github.io/tuvima_library/product/status/) page explains what is live, partial, or planned. For a detailed implementation view, see the [Feature Truth Inventory](https://tuvima.github.io/tuvima_library/product/feature-truth-inventory/).

## Try Tuvima

Tuvima currently targets developers and early adopters running it from source.

You will need:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A local copy of this repository
- Optional provider credentials for services that require them
- About 10 GB of free space if you want to use local AI models

Clone and restore:

```bash
git clone https://github.com/Tuvima/tuvima_library.git
cd tuvima_library
dotnet restore MediaEngine.slnx
```

Start the Engine and Dashboard in separate terminals:

```bash
dotnet run --project src/MediaEngine.Api
```

```bash
dotnet run --project src/MediaEngine.Web
```

Then open `http://localhost:5016`, add your folders in **Settings > Libraries**, and start your first scan.

The [Getting Started guide](https://tuvima.github.io/tuvima_library/tutorials/getting-started/) covers configuration, provider credentials, Docker, and troubleshooting. Continue with [Your First Library](https://tuvima.github.io/tuvima_library/tutorials/first-library/) for the full first-scan walkthrough.

## Learn More

Full user and developer documentation lives at [tuvima.github.io/tuvima_library](https://tuvima.github.io/tuvima_library/).

| If you want to... | Read... |
|---|---|
| Install and launch Tuvima | [Getting Started](https://tuvima.github.io/tuvima_library/tutorials/getting-started/) |
| Build your first library | [Your First Library](https://tuvima.github.io/tuvima_library/tutorials/first-library/) |
| Add and organize media | [How to Add Media](https://tuvima.github.io/tuvima_library/guides/adding-media/) |
| Understand how Tuvima identifies files | [How File Ingestion Works](https://tuvima.github.io/tuvima_library/explanation/how-ingestion-works/) |
| Configure metadata services | [Configure Providers](https://tuvima.github.io/tuvima_library/guides/configuring-providers/) |
| Check what is ready today | [Product Status](https://tuvima.github.io/tuvima_library/product/status/) |
| Explore the architecture | [Technical Overview](https://tuvima.github.io/tuvima_library/architecture/technical-overview/) |
| Fix a problem | [Troubleshooting](https://tuvima.github.io/tuvima_library/guides/troubleshooting/) |

## Contributing

Bug reports, feature ideas, documentation improvements, and code contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), [report a bug](https://github.com/Tuvima/tuvima_library/issues), or [request a feature](https://github.com/Tuvima/tuvima_library/issues).

The product is branded as **Tuvima Library**, while many projects and namespaces in the code still use the earlier `MediaEngine.*` name. They refer to the same product.

Tuvima is built on open-source software and public-knowledge projects. See [Attributions](https://tuvima.github.io/tuvima_library/reference/attributions/) for the maintained acknowledgement list.

## License

Tuvima Library is free and open-source software under the [GNU Affero General Public License v3.0](LICENSE).

---

<div align="center">

**You already own the stories. Tuvima makes them easier to find, understand, and enjoy.**

[Documentation](https://tuvima.github.io/tuvima_library/) ·
[Report a Bug](https://github.com/Tuvima/tuvima_library/issues) ·
[Request a Feature](https://github.com/Tuvima/tuvima_library/issues)

</div>
