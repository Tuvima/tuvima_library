---
title: "How to Add Media to Your Library"
summary: "Use watch folders, imports, and supported formats to bring media into Tuvima Library cleanly."
audience: "user"
category: "guide"
product_area: "library"
tags:
  - "watch-folders"
  - "import"
  - "media"
---

# How to Add Media to Your Library

This guide walks you through getting media files into Tuvima Library, whether you are adding one new file or importing a large existing collection.

---

## The two intake modes

Tuvima Library offers two ways to bring files in:

**Watch mode** monitors a folder continuously. When a new file appears, the Engine picks it up and processes it automatically.

**Import mode** performs a one-time scan of an existing folder. Use this when you already have a collection on disk and want to ingest it in bulk.

Many people use both: import the existing collection once, then leave watch mode enabled for future additions.

---

## Setting up a Library Folder

1. Open the Dashboard at `http://localhost:5016`.
2. Go to **Settings -> Library -> Library Folders**.
3. Click **Add Folder**.
4. Fill in:
   - **Source path**: the folder to watch or import
   - **Library root**: where organised files should live
   - **Category**: Books, Movies, TV, Music, or Comics
   - **Media types**: the file types you expect in that folder
   - **Watch mode**: on for continuous monitoring, off for one-time import
5. Click **Save**.

You can add as many folders as you need.

---

## Supported file formats

| Library type | Formats |
|---|---|
| Books | EPUB, PDF |
| Audiobooks | M4B, MP3 |
| Movies | MKV, MP4, AVI, MOV |
| TV | MKV, MP4 |
| Music | FLAC, MP3, AAC, M4A, OGG |
| Comics | CBZ, CBR, PDF |

> **MP3 and MP4 can be ambiguous.** The Engine uses file metadata, folder hints, and classification logic to decide whether a file is music, audiobook, movie, or TV. If it guesses wrong, you can correct it in the Vault.

---

## What happens when a file arrives

When the Engine finds a new file, it runs it through several stages automatically.

1. **Settle**: waits for the file to finish copying
2. **Fingerprint**: computes a SHA-256 identity for duplicate detection and resilient tracking
3. **Scan**: reads embedded metadata such as title, author, year, cover art, narrator, and series
4. **Classify**: resolves ambiguous media types when needed
5. **Stage**: moves the file into the safe staging area on disk
6. **Hydrate**: Stage 1 queries retail providers for cover art, descriptions, ratings, and bridge IDs; Stage 2 uses those IDs to resolve a Wikidata identity when possible
7. **Settle artwork and readiness**: the system decides whether the item has present art, missing art, or still-pending art
8. **Surface and promote**: main Vault visibility and final filesystem promotion are separate decisions

Two details matter here:

- An item can be known to the system before it is visible in the main Vault
- An item can be visible in the main Vault before or after final promotion into the organised library structure

---

## When an item appears in the main Vault

The main Vault only shows an item after it passes the Vault quality gate:

- real title
- resolved media type
- settled artwork outcome

If that gate is not satisfied yet, the item remains visible in Activity, Review, or the Action Center instead of appearing early in the main Vault.

---

## How the organised library is laid out

Once a file is promoted, it is placed into a folder structure inside your Library root. The exact layout depends on media type.

| Media type | Example path |
|---|---|
| Books | `Library/Books/Frank Herbert/Dune/Dune.epub` |
| Audiobooks | `Library/Audiobooks/Frank Herbert/Dune/Dune.m4b` |
| Movies | `Library/Movies/Dune (2021)/Dune (2021).mkv` |
| TV | `Library/TV/Dune Prophecy/Season 01/S01E01 - The Hidden Hand.mkv` |
| Music | `Library/Music/Hans Zimmer/Dune (Original Motion Picture Soundtrack)/01 - Dream Is a Collapsing Map.flac` |
| Comics | `Library/Comics/Dune/Dune House Atreides #01.cbz` |

The Engine handles the renaming and movement for you.

---

## Importing an existing collection

If you already have a large library on disk:

1. Add the folder in **Settings -> Library -> Library Folders**.
2. Leave **Watch mode** off unless you also want future monitoring.
3. Click **Import Now** on that folder.
4. Watch progress in the Dashboard while the Engine processes the files in the background.

During a large import, some items will first appear in Activity, Review, or the Action Center before they are ready for the main Vault. That is expected.

---

## Tips for best results

**Embedded metadata helps a lot.** Accurate titles, creators, and IDs such as ISBN or TMDB ID make good matches much easier.

**File naming matters less than you might think.** The Engine prefers what is inside the file over the filename when possible.

**Embedded covers are preserved.** The system may download better cover art, but it keeps track of the original artwork too.

**Duplicates are handled gracefully.** The fingerprint lets the Engine recognise the same file and avoid cluttering the library with accidental duplicates.

**Progress is live.** You can watch items move through the pipeline in real time.

## Related

- [Your First Library](../tutorials/first-library.md)
- [How File Ingestion Works](../explanation/how-ingestion-works.md)
- [How the Entire Pipeline Works](../explanation/how-the-pipeline-works.md)
- [Supported Media Types and Formats](../reference/media-types.md)
