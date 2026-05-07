---
title: "Your First Library"
summary: "Add a watch folder, ingest your first media, and learn how Tuvima organizes the result."
audience: "user"
category: "tutorial"
product_area: "library"
tags:
  - "watch-folders"
  - "library"
  - "onboarding"
---

# Your First Library

This tutorial walks you through adding your first watch folder and seeing your media appear in the Dashboard. By the end, you will understand the journey a file takes from your hard drive to the Library, and you will know how to read the status of each item.

**Prerequisites:** Tuvima Library is running (Engine and Dashboard both started). If not, see [Getting Started](getting-started.md) first.

---

## Step 1 â€” Open Settings and add a watch folder

In the Dashboard, click the gear icon in the left dock to open **Settings**, then select **Library Folders** from the sidebar.

Click **Add Folder**.

You will be asked for:

- **Folder path** â€” the full path to the folder on your machine. For example: `/media/books` or `D:\Media\Movies`.
- **Category** â€” what kind of media lives here. Choose from: Books, TV, Movies, Music, or Comics. This tells the Engine what to expect and which providers to contact for enrichment.
- **Media types** â€” the specific file types to watch for. The Engine suggests sensible defaults based on your category (for example, EPUB and PDF for Books, MKV and MP4 for Movies), but you can adjust these.

Click **Save**. The Engine immediately begins watching this folder.

> **Tip:** You can add as many folders as you like â€” one per category, or several folders for the same category. Each folder is watched independently.

---

## Step 2 â€” What happens when a file appears

The moment the Engine detects a new file in a watched folder, it starts a pipeline. Here is the journey every file takes:

### Settle

The Engine waits a few seconds after a file appears before touching it. This prevents it from processing a file that is still being copied or downloaded. Once the file size stops changing, the Engine considers it settled.

### Fingerprint

The Engine computes a unique identifier (a SHA-256 fingerprint) for the file. This fingerprint is the file's permanent identity in the Library. If you later rename the file or move it to a different folder, the Engine will recognise it as the same file.

### Scan

The Engine opens the file and reads the information embedded inside it â€” title, author, year, cover art, series name, and so on. For books, this means reading the EPUB metadata. For video files, it reads the embedded tags. The quality of this step depends on how well-tagged your files are.

### Identify

The Engine compares what it found in the file against its knowledge of known works. It scores possible matches and selects the best one. Weak matches are rejected, mid-confidence matches are sent to review, and only the strongest matches are auto-accepted.

### Stage

The file is registered and enrichment begins immediately, but the main browse surfaces does not show it yet. Items stay in Activity, Review, and the Review Queue until they have a trustworthy title, a resolved media type, and a settled artwork outcome.

### Hydrate

Two enrichment stages happen automatically:

**Stage 1 â€” Retail providers:** The Engine contacts services like Apple Books, Google Books, and TMDB to gather cover art, descriptions, ratings, and bridge identifiers (such as ISBN or TMDB ID).

**Stage 2 â€” Wikidata:** Using the bridge identifiers from Stage 1, the Engine contacts Wikidata to fetch the canonical version of the metadata â€” the authoritative title, author, director, series information, and publication year. Wikidata is the Engine's source of truth for all structured data.

### Promote

Once enrichment is complete enough for the browse readiness gate, the item appears in the main browse surfaces. Full organization and browse-page promotion continue separately based on the wider pipeline and library confidence rules.

---

## Step 3 â€” Watch your files appear in the current media surfaces

Click **media library** in the left dock. This is the command centre for everything in your Library.

You will see qualified files appearing as the pipeline settles. Each row shows:

- **Thumbnail** â€” the cover art (or a placeholder while it is being fetched)
- **Title and creator** â€” the best available title and author/director
- **Universe** â€” the franchise or creative world this item belongs to (if known)
- **Pipeline status** â€” small dots showing which pipeline stages are complete (hover over them for detail)
- **Status pill** â€” the current state of this item

Items do not enter the main browse surfaces immediately on file registration anymore. Review-only or still-pending items stay in the Review Queue until they are either ready for the main browse surfaces or explicitly need your intervention.

the current media surfaces updates in real time â€” you do not need to refresh the page.

---

## Step 4 â€” Understanding status indicators

Every item in the current media surfaces has a status pill that tells you where it stands.

| Status | What it means |
|---|---|
| **Verified** | The Engine is confident about this item's identity. It has been matched against Wikidata and enriched with canonical metadata. |
| **QID Not Found** | Retail matching succeeded, but Stage 2 could not resolve a Wikidata QID. The item may still be visible in the current media surfaces if its title, media type, and artwork state are settled. |
| **Needs Review** | Something went wrong or the Engine is unsure and wants your input. See below. |
| **Quarantined** | The file has a problem (corrupt, unreadable, or a type mismatch) and has been set aside. |
| **Pending** | The item is still moving through the pipeline. |

---

## Step 5 â€” Resolving "Needs Review" items

A **Needs Review** status means the Engine ran into a situation it could not resolve on its own. Common reasons include:

- Two or more Wikidata entries matched equally well and the Engine could not decide between them
- The file metadata contained conflicting information
- The media type could not be determined automatically
- The retail provider returned no results and Wikidata found nothing with sufficient confidence

To resolve a Needs Review item:

1. Click the item's row in the current media surfaces to open the **Detail Drawer** on the right.
2. Scroll to the **Pipeline** section.
3. Look at the **Retail** and **Wikidata** stage entries. Each shows the candidates the Engine found, along with match scores.
4. Pick the correct candidate, or use the manual search to find the right one.
5. If nothing matches (for example, an obscure self-published title), click **Add Provisional** â€” this pre-fills a form with the file's own metadata for you to correct and confirm.

Once you resolve an item, the Engine re-runs enrichment with your input. The status updates automatically.

> **Items that are still uncertain are not forced into the main browse surfaces.** They remain visible in Review, Activity, and the Review Queue until they either pass the browse readiness gate or you resolve them manually.

---

## Step 6 â€” Browse your Library

Once files are promoted, they appear on the **Home** page and on the media lane pages (Books, TV, Movies, and so on).

The **Home** page surfaces personalised swimlanes â€” recently added items, things the Engine thinks you might like, and smart groupings based on genre, mood, or creator.

The media lane pages (accessible from the left dock) show everything in a given category, organised into swimlanes by Universe, series, genre, or whatever grouping makes sense for that media type.

Click any item to see its detail page, or return to the current media surfaces to manage your collection.

---

## What to do next

- Add more watch folders in Settings â†’ Library Folders.
- If you have items stuck in Needs Review, work through them in the current media surfaces.
- Explore the Settings â†’ Providers screen to configure which external services the Engine uses for enrichment.
- Read [How File Ingestion Works](../explanation/how-ingestion-works.md) for a deeper explanation of the decisions the Engine makes along the way.

## Related

- [How to Add Media to Your Library](../guides/adding-media.md)
- [How to Resolve Items That Need Review](../guides/resolving-reviews.md)
- [How File Ingestion Works](../explanation/how-ingestion-works.md)

