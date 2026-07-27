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

Personal media collections rarely live in one neat place. A single story might be an ebook in one folder, an audiobook in another, a film on a hard drive, and a soundtrack mixed into a music collection. Most media software can make each file type look good, but the connection between them is usually left for you to remember or recreate.

Tuvima Library starts with the story.

Point Tuvima at the folders you choose and it builds a rich, browsable library around them. It identifies each item, adds useful metadata and artwork, connects related works where it has trustworthy evidence, and remembers your progress. Instead of searching through folders and filenames, you can explore the ideas, people, series, and creative worlds represented by the media you own.

## One Story, Every Form

Imagine owning the *Dune* novels as EPUBs, their audiobook editions as M4Bs, two film adaptations as MKVs, a graphic novel as a CBZ, and a soundtrack as a FLAC album.

Those files still belong in Read, Watch, and Listen, because each format deserves an experience designed for it. But they should not become unrelated islands. Tuvima gives them a common identity and relationship layer, allowing the library to understand that they are different expressions of the same larger creative world.

That common view gives a collection life:

- A file becomes a work with a title, artwork, description, creators, performers, and history.
- Different formats of the same work can be understood as versions rather than unrelated duplicates.
- Books, films, shows, albums, comics, and audiobooks can retain their own identity while revealing how they are connected.
- Progress and ownership make the view personal: what you have, what you started, what you finished, and what belongs next.
- New media can deepen an existing shelf or reveal a broader Collection without requiring you to rebuild the library by hand.

Read, Watch, and Listen are therefore different doors into the same library—not three disconnected catalogs.

## Collections That Build Themselves

Tuvima uses a simple principle: immediate groups should be useful, while broader Collections should earn their place.

- A book series becomes an ordered shelf in Read.
- A film series or TV show becomes a shelf in Watch.
- An album or audiobook series becomes a shelf in Listen.
- A broader Collection appears only when trusted metadata connects multiple shelves through a real series, franchise, or creative-world relationship.

For example, owning only a film trilogy creates a useful Watch shelf. Owning related novels, film series, audiobooks, and music can create a broader Collection that brings those shelves together. An ebook and audiobook of one title do not create a Collection by themselves; they are two owned ways to experience the same work.

These structural groupings are automated. Tuvima uses file metadata and configured knowledge sources to identify relationships, then updates the view as the library changes. It does not rely on similar titles alone, and uncertain matches are sent for review rather than silently forcing unrelated items together.

Richer personal rules, recommendations, and smart collection automation remain in development. Learn more in [How Universes and Series Work](https://tuvima.github.io/tuvima_library/explanation/how-universes-work/).

## Open Knowledge Gives the Library Context

[Wikidata](https://www.wikidata.org/wiki/Wikidata:Introduction) is one of the most remarkable resources behind Tuvima. It is a free, collaborative, multilingual knowledge base maintained by people around the world. More importantly for a library, it describes not only *things*, but the relationships between them.

A metadata provider can help Tuvima identify a file as a particular book, film, album, or episode. Wikidata helps answer the next questions:

- Is this work part of a series or a wider franchise?
- Is this film an adaptation of a book already in the library?
- Which people created, performed, directed, narrated, or composed it?
- Which editions and formats represent the same work?
- What other owned shelves belong to the same creative world?

Tuvima approaches that information carefully. It first looks for a safe media match and a trustworthy identifier, such as an ISBN, TMDB ID, MusicBrainz ID, or Comic Vine ID. It can then use that identifier to find the corresponding Wikidata item and follow supported relationships. Those connections help build ordered shelves, people views, adaptations, and cross-media Collections without relying on title similarity alone.

If a reliable identity or relationship is not available, Tuvima does not use Wikidata as a guessing engine. The item remains usable with its other metadata, or waits for review when human confirmation is needed.

### Giving Back to Wikidata

Tuvima does not see Wikidata as simply a free API to consume. It is shared public infrastructure, built through an extraordinary amount of community effort, and the project wants to support it wherever possible.

That means:

- Clearly attributing Wikidata and linking people back to the source.
- Preserving where facts came from, when they were retrieved, and whether Tuvima changed or summarized them.
- Querying and caching data responsibly.
- Making data gaps and uncertain relationships visible instead of hiding them.
- Contributing corrections, modeling improvements, documentation, and useful open tooling back to the community where appropriate.
- Encouraging Tuvima contributors and users to [participate in Wikidata](https://www.wikidata.org/wiki/Wikidata:Contribute) or [support the Wikimedia movement](https://donate.wikimedia.org/).

Wikidata makes Tuvima's common view possible. Helping that knowledge become more complete, accurate, and useful benefits Tuvima, the Wikimedia projects, and everyone else building with open knowledge.

## From Files to a Living Library

### Bring the collection you already have

Keep your existing books, comics, movies, TV episodes, music, and audiobooks on your own disks. Tuvima watches the folders you configure and notices when media is added or changed.

### Let Tuvima organize the details

Tuvima reads the files, identifies their contents, and enriches them with titles, descriptions, credits, artwork, series information, and other useful context. It then uses those identities to build shelves and Collections. Confident matches flow into the library automatically; uncertain items wait in a Review Queue for you.

### Find something worth returning to

Home helps you continue where you stopped and rediscover the collection. Read, Watch, and Listen give each kind of experience a natural home, while search reaches across the whole library. Series, creators, and broader collections help reveal connections that folders alone cannot.

### Enjoy it without giving up control

Read supported books, play audio and video, track progress, and correct an item from its own page. Your library remains yours: the catalog, artwork, progress, and optional AI processing stay on your machine.

## What Tuvima Does Differently

[Plex](https://support.plex.tv/articles/200288286-what-is-plex/), [Jellyfin](https://jellyfin.org/), and [Audiobookshelf](https://audiobookshelf.org/docs/documentation/introduction/) are strong products. They also begin from different goals.

| Product | What it does especially well | Where Tuvima goes further |
|---|---|---|
| Plex | Mature streaming across many devices, with polished movie, TV, music, and photo libraries | Tuvima connects books, comics, audiobooks, music, films, and TV through the stories and creative worlds they share instead of leaving them in media-type libraries or name-matched collections |
| Jellyfin | Free, private, multi-device media streaming with broad playback and library support | Tuvima puts the story, its different versions, its place in a series, and trusted connections to other media at the center of the product |
| Audiobookshelf | A focused audiobook and podcast experience with chapters, progress, metadata tools, and companion apps | Tuvima connects an audiobook to its ebook, adaptations, soundtrack, creators, series, and wider Collection instead of stopping at its place in a book library |
| Tuvima Library | A common, local-first view of the stories and creative worlds represented by everything you own | Cross-media understanding is the foundation, not an add-on: the library can build and evolve its structure as it learns what each item is |

Plex and Jellyfin are excellent when the main goal is mature streaming to many devices. Audiobookshelf is excellent when the audiobook itself is the center of the experience. Tuvima is built for the collector who wants all of those formats to participate in one growing, meaningful view.

Its advantage is not simply supporting more file extensions. It is understanding that the files are related—and using that understanding to make the whole collection more valuable than the sum of its folders.

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
- Richer recommendations, playlists, personal rules, and smart collections.
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
| See how open knowledge connects media | [How Universes and Series Work](https://tuvima.github.io/tuvima_library/explanation/how-universes-work/) |
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
