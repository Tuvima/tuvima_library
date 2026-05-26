---
title: "Your First Library"
summary: "Add a folder, scan media, watch ingestion progress, and understand how Tuvima organizes the result."
audience: "user"
category: "tutorial"
product_area: "library"
tags:
  - "watch-folders"
  - "library"
  - "onboarding"
---

# Your First Library

This tutorial walks through the first practical loop: configure a folder, scan it, watch ingestion, and resolve anything Tuvima cannot identify safely.

**Prerequisite:** The Engine and Dashboard are running. If not, start with [Getting Started](getting-started.md).

## Step 1 - Open Library Settings

In the Dashboard, open **Settings > Libraries**.

Confirm the current folder settings:

- **Watch Folder** - where new files appear for Tuvima to scan.
- **Library Root** - where organized files can live after promotion.
- **Organization template** - the folder/file naming pattern used during organization.
- **Path checks** - whether the Engine can read and write the configured paths.

Save the settings when they look correct.

## Step 2 - Add Test Media

Copy a small set of supported files into the Watch Folder. Start with a few known items rather than a huge collection:

- one EPUB or PDF
- one movie or TV file
- one album track or audiobook
- one comic archive if you use comics

Supported formats are listed in [Media Types](../reference/media-types.md).

## Step 3 - Start A Scan

Start the first scan from **Settings > Libraries > Scan saved watch folder**.

Then open **Settings > Ingestion**.

## Step 4 - Watch The Pipeline

When a file is discovered, the Engine moves through these broad stages:

1. **Settle** - wait until the file is no longer being copied.
2. **Fingerprint** - compute a stable file identity for duplicate detection.
3. **Scan** - read embedded metadata and artwork where possible.
4. **Classify** - resolve ambiguous formats such as MP3, M4A, MP4, MKV, AVI, or WEBM.
5. **Identify** - compare file data with known works and provider candidates.
6. **Retail stage** - gather cover art, descriptions, ratings, and bridge IDs from configured providers.
7. **Wikidata stage** - use bridge IDs to resolve canonical identity when possible.
8. **Readiness** - decide whether the item is ready for Home, Read, Watch, Listen, Search, or Collections.

SignalR updates the Dashboard while ingestion is running, so you should not need to refresh the page.

## Step 5 - Understand Where Items Appear

Items do not appear everywhere immediately.

- **Home** shows discovery and overview shelves returned by the Engine.
- **Read** shows books and comics.
- **Watch** shows movies and TV.
- **Listen** shows music and audiobooks.
- **Search** finds library items across media lanes.
- **Collections** shows broader rollups and managed collections when they are backed by real data.
- **Review Queue** holds items that need human confirmation.

An item is eligible for browse surfaces only after it has a real title, resolved media type, and settled artwork outcome. Items that are uncertain stay in Review Queue instead of being shown as if they were correct.

## Step 6 - Resolve Review Items

Open **Settings > Review Queue** if the Ingestion dashboard shows items needing attention.

Common reasons include:

- no reliable retail match
- multiple plausible candidates
- conflicting embedded metadata
- missing bridge IDs for Wikidata
- uncertain media type
- corrupt or unreadable files

Open the item, review the reason, and launch the shared editor. Review changes are applied through Engine APIs and the current surface refreshes after a successful edit.

## Step 7 - Browse The Result

After the readiness gate passes, open:

- **Home** for overview shelves.
- **Read**, **Watch**, or **Listen** for media-specific browsing.
- **Search** to find the item by title, creator, album, or other indexed fields.
- The item's detail page to inspect metadata and make inline corrections.

Tuvima only shows real data returned by the Engine. Empty shelves, unavailable AI states, and missing provider results are not replaced with fake examples.

## Related

- [How to Add Media to Your Library](../guides/adding-media.md)
- [How File Ingestion Works](../explanation/how-ingestion-works.md)
- [How to Resolve Review Items](../guides/resolving-reviews.md)
- [Troubleshooting](../guides/troubleshooting.md)
