---
title: "Your First Library"
summary: "Add a governed source, begin catalogued or personal-media intake, and understand where Tuvima shows the result."
audience: "user"
category: "tutorial"
product_area: "library"
tags:
  - "watch-folders"
  - "library"
  - "onboarding"
---

# Your First Library

This tutorial walks through the first practical loop: configure a governed
source, begin the appropriate intake path, watch catalogued ingestion when
applicable, and understand where the result appears.

**Prerequisite:** The Engine and Dashboard are running. If not, start with [Getting Started](getting-started.md).

## Step 1 - Open Library Settings

In the Dashboard, open **Settings > Libraries**.

Create or select a library, then confirm:

- **Watch Folder** - where new files appear for Tuvima to scan.
- **Primary destination** - the managed source where organized files can live after intake.
- **Organization template** - the folder/file naming pattern used during organization.
- **Path checks** - whether the Engine can read and write the configured paths.
- **Library kind** - catalogued for administrator-created Read, Watch, and Listen libraries.
- **Metadata policy** - enriched for known catalogue works.

Save the settings when they look correct.

## Step 2 - Add Test Media

Copy a small set of supported files into the Watch Folder. Start with a few known items rather than a huge collection:

- one EPUB or PDF
- one movie or TV file
- one album track or audiobook
- one comic archive if you use comics
- a few mixed local files if you configured a source for your View Personal Space

For photos, home videos, documents, lectures, audio notes, or private unmatched
content, enable View for the owning profile. Under **Settings > Users**, choose
**Import folder** to copy files into that profile's managed source directory, or
**Link existing folder** to index an external folder read-only. Tuvima will not
send those items through retail providers, Wikidata, or identity review.

Supported formats are listed in [Media Types](../reference/media-types.md).

## Step 3 - Begin The Correct Intake Path

For a catalogued Read, Watch, or Listen library, start the administrator scan
from **Settings > Libraries** when importing an existing folder. Then
open **Settings > Ingestion**.

For View, select the owning profile and open `/view`. The profile's Personal
Space is the user-facing destination; sources and devices are provenance, not
library choices. Browser upload resolves that space automatically. View
reconciliation exists only for administrator recovery/diagnostics and is not the
normal Photos workflow.

## Step 4 - Watch The Pipeline

For catalogued media, the Engine moves through these broad stages:

1. **Settle** - wait until the file is no longer being copied.
2. **Fingerprint** - compute a stable file identity for duplicate detection.
3. **Scan** - read embedded metadata and artwork where possible.
4. **Classify** - resolve ambiguous formats such as MP3, M4A, MP4, MKV, AVI, or WEBM.
5. **Identify** - compare file data with known works and provider candidates.
6. **Stage 3 retail metadata & primary artwork** - gather primary cover/poster evidence, descriptions, ratings, people seeds, and bridge IDs from configured providers.
7. **Stage 4 Wikidata** - use bridge IDs to resolve canonical identity when possible.
8. **Stage 5 file ready** - store core canonical values and managed artwork.
9. **Stages 6-8 enrichment** - expand people, universe relationships, lyrics/subtitles, and deeper artwork.
10. **Readiness** - decide whether the item is ready for Home, Read, Watch, Listen, Search, or Collections.

SignalR tells the Dashboard when to refresh the ingestion snapshot while ingestion is running, so you should not need to refresh the page.

Personal View media takes a separate local-only path: settle, fingerprint,
extract safe local metadata, retain source/device paths, group compound files,
and update the local search/timeline index. It does not enter retail matching,
Wikidata, canonical claims, or Review Queue.

## Step 5 - Understand Where Items Appear

Items do not appear everywhere immediately.

- **Home** shows discovery and overview shelves returned by the Engine.
- **Read** shows books and comics.
- **Watch** shows movies and TV.
- **Listen** shows music and audiobooks.
- **Search** finds library items across media lanes.
- **Collections** organizes automatic broader rollups, published curated collections, lane-level shelves, and people when they are backed by real library data.
- **View** exposes exactly Photos, Galleries, People, and Places. Photos uses
  the saved authorized scope; first use prefers Shared View when permitted and
  Mine otherwise. Revoked saved scopes fall back to Shared when still
  permitted, then Mine. People and Places remain truthful capability states in
  the current Dashboard even though evidence-based Engine queries exist.
- **Review Queue** holds items that need human confirmation.

A catalogued item is eligible for browse surfaces only after it has a real
title, resolved media type, and settled artwork outcome. Items that are
uncertain stay in Review Queue instead of being shown as if they were correct.
Personal View assets are available from local index state and never enter that
catalogue readiness/review gate.

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
