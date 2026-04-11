---
title: "How File Ingestion Works"
summary: "Follow the path from a raw file on disk to a structured item inside Tuvima Library."
audience: "user"
category: "explanation"
product_area: "ingestion"
tags:
  - "ingestion"
  - "pipeline"
  - "watchers"
---

# How File Ingestion Works

When a new file lands in one of your watched folders, Tuvima Library doesn't just copy it somewhere and call it done. It goes through a careful, multi-stage journey â€” from raw unknown file to fully identified, enriched, and organized library entry. This page walks you through every step of that journey and explains the reasoning behind each one.

---

## The Big Picture

The ingestion pipeline is designed around one core idea: **trust must be earned**. A file isn't added to your library until the Engine has enough confidence in what it is. Until that threshold is reached, the file stays in a safe holding area (`.data/staging/`) where it can be reviewed, corrected, or retried without affecting the rest of your library.

Here's the complete journey:

```
File appears in watched folder
  â†’ Settle (wait for copy to finish)
  â†’ Lock check (confirm file is fully written)
  â†’ Fingerprint (SHA-256 identity)
  â†’ Scan (extract embedded metadata)
  â†’ Classify (resolve ambiguous formats)
  â†’ Identify (match against known works)
  â†’ Stage (hold in .data/staging/)
  â†’ Hydrate (enrich from external sources)
  â†’ Promote (move to organized library location)
```

---

## Step 1: Detection and Settling

The Engine uses a FileSystemWatcher to monitor your configured library folders. The moment a file appears, it's noticed â€” but not immediately acted on.

Why the wait? Large files (a 50 GB Blu-ray remux, an audiobook M4B) may take several minutes to finish copying. If the Engine started scanning a half-written file, it would get garbage. So it watches for the file to stop growing, waits for a configurable settle period, and only then moves forward.

After settling, a **lock check** confirms the file isn't still being held open by another process. Only once the file is fully and exclusively accessible does ingestion begin.

---

## Step 2: Fingerprinting

The Engine computes a SHA-256 hash of the file's contents. This hash is the file's permanent identity â€” its fingerprint.

This is more powerful than it might seem. If you rename the file, move it to a different folder, or reorganize your collection, the Engine still recognizes it by its hash. It won't treat the same file as a new arrival just because the filename changed.

More importantly, this is how **duplicate detection** works. If the hash already exists in the data store, the Engine knows it's seen this file before. Instead of creating a duplicate entry, it creates a new Edition under the existing Work. You might have the same novel as both a standard EPUB and a DRM-free PDF â€” they're different files, same fingerprint-checked Work, coexisting as separate Editions.

---

## Step 3: Scanning

Once fingerprinted, the file is handed to a **processor** â€” a component that knows how to read a particular file type.

Processors are checked in priority order and use **magic byte detection** to claim files, not file extensions. This matters because file extensions can be wrong, renamed, or misleading. Magic bytes are the first few bytes of the file itself and reliably identify the true format regardless of what the filename says.

| Processor | Priority | What it reads |
|---|---|---|
| EPUB | 100 | Ebooks â€” extracts OPF metadata, cover art, author, series |
| Audio | 95 | M4B, MP3 â€” reads ID3/iTunes tags, embedded artwork |
| Video | 90 | MKV, MP4 â€” reads container metadata, subtitle tracks |
| Comic | 85 | CBZ, CBR â€” reads ComicInfo.xml if present |
| Generic | fallback | Anything else â€” filename parsing only |

The processor extracts embedded metadata â€” title, author, year, cover art, series name, narrator, ISBN â€” and emits these as **Claims**. Each Claim carries a source and a confidence score. A title found in a well-structured EPUB OPF file gets higher confidence than one guessed from a messy filename. These Claims feed the Priority Cascade described in [how-scoring-works.md](how-scoring-works.md).

---

## Step 4: Classifying Ambiguous Formats

Some file types are genuinely ambiguous. An MP3 file could be a music track or an audiobook chapter. An MP4 could be a movie, a TV episode, a short film, or a recorded lecture.

The Engine uses an AI component called the **MediaTypeAdvisor** to resolve this. It examines tag signals: the genre tag, whether a narrator field is present, whether an ASIN (Amazon's audiobook identifier) is embedded, and whether the duration is consistent with an audiobook (typically 30+ minutes per file) or a music track (3â€“5 minutes).

The library folder's configured category provides an additional prior. If you've told the Engine that a folder contains audiobooks, a file in that folder gets a confidence boost toward the audiobook classification. The AI's signal combines with this prior to make the final call.

---

## Step 5: Identification

With metadata in hand, the Engine searches for a match. It uses the extracted title, author, ISBN, and other signals to query retail providers (Stage 1 of the enrichment pipeline).

Candidates come back ranked by how well they match. The Priority Cascade scores them. If the top candidate scores at or above **0.85 confidence**, the file is automatically linked to that Work. Below that threshold, the item enters the **review queue** â€” visible in the Vault under "Needs Review" â€” where you can make the call manually.

One important nuance: **field count scaling**. A file with only a title and nothing else gets penalized. A sparse match is inherently less trustworthy than a match across five or six metadata fields. This prevents near-empty files from confidently auto-linking to wrong Works.

---

## Step 6: Staging

All files land in `.data/staging/{assetId12}/` first â€” every single one, regardless of confidence. This flat structure (one subfolder per asset, named by a 12-character asset ID) is the Engine's safe holding area.

The data store tracks each file's status. There are no subcategory folders for "pending" or "low-confidence" â€” the database handles all of that. The only special folder is `staging/rejected/` for files you've explicitly rejected.

This staging-first approach means the Engine can always recover. If enrichment fails halfway through, the file is still there. If you correct a misidentification, the Engine can re-run from staging without touching your organized library.

---

## Step 7: Hydration

Once staged, the file enters the two-stage enrichment pipeline. Stage 1 pulls cover art, descriptions, and ratings from retail providers. Stage 2 uses Wikidata to fetch canonical structured data â€” author, genre, series, franchise relationships, and person data.

This is described in detail in [how-hydration-works.md](how-hydration-works.md).

---

## Step 8: Promotion

Once a file is hydrated and its confidence meets the threshold (â‰¥0.85), it's **promoted** from staging to its final organized location using a media-type-specific path template.

Examples:
- Books: `Books/{Author}/{Title} ({QID})/{Title}.epub`
- Audiobooks: `Audiobooks/{Author}/{Title} ({QID})/`
- TV: `TV/{Show}/Season {N}/{SxxExx} - {Title}.mkv`
- Music: `Music/{Artist}/{Album}/{Track}.mp3`

Note: the `{Format}` token was intentionally removed from all templates. It caused duplicate subfolder nesting when the same title existed in multiple formats. Format is now handled at the Edition level in the data store, not the filesystem.

---

## Step 9: Work-Level Deduplication

This runs at the identification step, but deserves its own explanation because it's central to how the library model works.

The `MediaEntityChainFactory` checks for an existing Work before creating a new one. A Work is identified by the combination of title, author/creator, and media type. If a match exists:

- A new **Edition** is created under the existing Work
- The new file is linked as a **Media Asset** of that Edition
- No duplicate Work is created

This is how the library handles multiple versions gracefully. The hardback and the paperback of the same novel, the theatrical cut and the director's cut of the same film, the standard MP3 and the lossless FLAC of the same album â€” all coexist as Editions under one Work, not as separate entries cluttering your library.

---

## When Things Go Wrong

The pipeline is designed to fail gracefully:

- **File won't settle**: Stays in the watch queue indefinitely. No orphans.
- **Processor can't read the file**: Falls back to filename parsing. Lower confidence, but still enters the pipeline.
- **Identification confidence too low**: Goes to "Needs Review" in the Vault. You resolve it manually.
- **Enrichment fails**: File stays in staging with its current metadata. A 30-day refresh cycle will retry.
- **File moves or is renamed after staging**: The SHA-256 fingerprint means the Engine recognizes it on its next scan.

---

For technical details about the ingestion pipeline â€” queue management, folder configuration, path template syntax, and the full staging directory layout â€” see the [architecture deep-dive](../architecture/ingestion-pipeline.md).

## Related

- [Ingestion Pipeline](../architecture/ingestion-pipeline.md)
- [How to Add Media to Your Library](../guides/adding-media.md)
- [Supported Media Types and Formats](../reference/media-types.md)
