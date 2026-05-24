---
title: "How to Add Media to Your Library"
summary: "Use configured folders, scans, and supported formats to bring media into Tuvima Library cleanly."
audience: "user"
category: "guide"
product_area: "library"
tags:
  - "watch-folders"
  - "import"
  - "media"
---

# How to Add Media to Your Library

Tuvima Library brings files in through configured folders. You can leave a folder watched for ongoing additions, or use scans to import existing files in batches.

## Choose A Folder Strategy

**Watch folder workflow** is best for day-to-day use. Put new files in the watched folder and Tuvima picks them up automatically or during the next scan.

**Batch import workflow** is best for an existing collection. Point Tuvima at a folder, scan it, and work through any review items before adding more.

For large existing libraries, start with one media lane at a time. It is easier to tune providers and review rules with a smaller batch.

## Configure Folders

1. Open the Dashboard at `http://localhost:5016`.
2. Go to **Settings > Libraries**.
3. Set the Watch Folder and Library Root.
4. Confirm path checks show the Engine can read and write where required.
5. Save the settings.
6. Run **Scan saved watch folder** or return to **Settings > Setup** and use **Scan Now**.

Open **Settings > Ingestion** to monitor progress.

## Supported Formats

| Lane | Media | Formats |
|---|---|---|
| Read | Books | EPUB, PDF |
| Read | Comics | CBZ, CBR |
| Watch | Movies | MKV, MP4, M4V, WEBM, AVI |
| Watch | TV | MKV, MP4, M4V, WEBM, AVI |
| Listen | Music | FLAC, MP3, AAC, M4A, OGG, WAV |
| Listen | Audiobooks | M4B, MP3, M4A |

MP3, M4A, MP4, MKV, AVI, and WEBM can be ambiguous. Tuvima uses folder context, embedded metadata, filename patterns, and classification logic to decide whether a file is music, audiobook, movie, or TV. If it cannot decide safely, the item goes to Review Queue.

## What Happens When A File Arrives

1. **Settle:** wait for file activity to stop.
2. **Fingerprint:** compute a stable identity for duplicate detection.
3. **Scan:** read embedded metadata and artwork.
4. **Classify:** resolve media type where needed.
5. **Stage:** register the file safely before promotion.
6. **Hydrate:** call configured providers for metadata and bridge IDs.
7. **Resolve Wikidata:** use bridge IDs for canonical identity when possible.
8. **Settle artwork:** decide whether artwork is present, missing, or still pending.
9. **Surface:** show the item only where it is ready and backed by real data.

## When Items Become Visible

Home, Read, Watch, Listen, Collections, and Search show items that pass the browse readiness gate:

- non-placeholder title
- resolved media type
- settled artwork outcome

Items that do not pass stay visible in operational surfaces such as Ingestion, Activity, or Review Queue.

## Tips For Better Matches

- Keep embedded metadata when possible.
- Include identifiers such as ISBN, ASIN, TMDB ID, MusicBrainz ID, or Comic Vine ID when your tools support them.
- Put mixed file types in folders with clear intent.
- Use provider credentials for services that require them.
- Start with small batches and resolve review items before importing thousands of files.

## Related

- [Your First Library](../tutorials/first-library.md)
- [Supported Media Types and Formats](../reference/media-types.md)
- [How File Ingestion Works](../explanation/how-ingestion-works.md)
- [Troubleshooting](troubleshooting.md)
