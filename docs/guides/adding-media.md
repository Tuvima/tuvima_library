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

This guide walks you through getting media files into Tuvima Library â€” whether you're adding a single new file or bringing in an existing collection of thousands.

---

## The two intake modes

Tuvima Library offers two ways to bring files in:

**Watch mode** monitors a folder on an ongoing basis. The moment a new file appears â€” whether you copied it, downloaded it, or moved it there â€” the Engine picks it up and processes it automatically. Use this for your active download or import folders.

**Import mode** performs a one-time scan of an existing folder. Use this for a collection that's already on your drive and won't be changing.

You can run both modes on the same folder if you want. A typical setup is: Import once to ingest your existing collection, then enable Watch so future additions are handled automatically.

---

## Setting up a Library Folder

Before the Engine can watch or import anything, you need to tell it where your files are.

1. Open the Dashboard at `http://localhost:5016`.
2. Go to **Settings â†’ Library â†’ Library Folders**.
3. Click **Add Folder**.
4. Fill in the folder details:
   - **Source path** â€” the folder on your drive to watch or import (e.g. `D:\Downloads\Books`).
   - **Library root** â€” where the organised library should live after files are processed (e.g. `D:\Library`). This is where Tuvima moves files once they've been identified.
   - **Category** â€” the type of media in this folder (Books, Movies, TV, Music, Comics).
   - **Media types** â€” the specific formats to accept (e.g. EPUB and PDF for a Books folder). You can select multiple.
   - **Watch mode** â€” toggle this on if you want the folder monitored continuously.
5. Click **Save**.

You can add as many folders as you need â€” one for each category, or one per drive, or however your collection is organised.

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

> **Note on MP3 and MP4:** These formats can belong to more than one library type â€” an MP3 might be an audiobook chapter or a music track; an MP4 might be a movie or a TV episode. The Engine uses AI classification and file metadata signals to decide. If it gets it wrong, you can correct it in the Library Vault.

---

## What happens when a file arrives

When the Engine finds a new file, it runs it through several stages automatically. You don't need to do anything â€” this all happens in the background.

1. **Settle** â€” The Engine waits a moment to make sure the file has finished copying before touching it. This prevents half-written files from being processed.

2. **Fingerprint** â€” A unique identifier is computed from the file's contents (a SHA-256 hash). This acts like a barcode â€” the Engine can track the file even if you rename or move it later.

3. **Scan** â€” The file is opened and any embedded information is read out: title, author, year, cover art, series name, narrator, and so on. Different processors handle different formats (EPUBs, ID3 tags in MP3s, MKV metadata, and so on).

4. **Identify** â€” The Engine scores the metadata it found and tries to match the file to an existing entry in your data store or to a known external source.

5. **Stage** â€” The file is held temporarily while enrichment runs.

6. **Hydrate** â€” Two rounds of enrichment fill in missing or uncertain metadata. Stage 1 queries retail providers (Apple, Google Books, TMDB, and others) to gather cover art, ratings, and identifiers. Stage 2 uses Wikidata to confirm the canonical identity and fetch structured data: author, director, series, genre, and more.

7. **Promote** â€” If the Engine is sufficiently confident (85% or above), the file is moved from staging into your organised library folder. If confidence falls below that threshold, the item is flagged for your review in the Library Vault.

---

## How the organised library is laid out

Once a file is promoted, it is placed into a folder structure inside your Library root. The layout follows a consistent template for each media type:

| Media type | Example path |
|---|---|
| Books | `Library/Books/Frank Herbert/Dune/Dune.epub` |
| Audiobooks | `Library/Audiobooks/Frank Herbert/Dune/Dune.m4b` |
| Movies | `Library/Movies/Dune (2021)/Dune (2021).mkv` |
| TV | `Library/TV/Dune Prophecy/Season 01/S01E01 - The Hidden Hand.mkv` |
| Music | `Library/Music/Hans Zimmer/Dune (Original Motion Picture Soundtrack)/01 - Dream Is a Collapsing Map.flac` |
| Comics | `Library/Comics/Dune/Dune House Atreides #01.cbz` |

The Engine handles the renaming and moving. You do not need to pre-organise your files before dropping them in.

---

## Importing an existing collection

If you have thousands of files already sitting on a drive, use Import mode to ingest them all at once.

1. Add the folder in **Settings â†’ Library â†’ Library Folders** as described above.
2. Leave **Watch mode** off (unless you also want ongoing monitoring).
3. Click **Import Now** on the folder. The Engine begins scanning all files in that folder and all subfolders.
4. Progress is reported in real time on the Dashboard. Items that need your attention will appear in the Library Vault.

Large collections may take some time to process, especially during the enrichment stages where the Engine queries external providers. The Engine processes files in parallel and will keep going in the background while you use the Dashboard.

---

## Tips for best results

**File naming matters less than you might think.** The Engine reads metadata embedded inside the file, not just the filename. A file called `untitled_book_final_v3.epub` will be identified correctly if its internal metadata says "Dune" by Frank Herbert.

**Embedded metadata helps a lot.** If your files have accurate titles, authors, and ISBNs or other identifiers embedded in their tags, the Engine will identify them faster and more confidently.

**Covers in the file are preserved.** Any cover art already embedded in a file is kept. The Engine may also download higher-quality cover art from providers, but your original artwork is never discarded.

**Duplicates are handled gracefully.** If you add a file that the Engine has seen before (same fingerprint), it is recognised as a duplicate. If you add a different edition of a Work already in your library â€” say, a new audiobook version of a book you already have â€” the Engine creates a new Edition under the existing Work rather than treating it as a completely separate item.

**You can watch the progress live.** The Dashboard updates in real time as files move through the pipeline. No page refresh needed.

## Related

- [Your First Library](../tutorials/first-library.md)
- [Supported Media Types and Formats](../reference/media-types.md)
- [How File Ingestion Works](../explanation/how-ingestion-works.md)
