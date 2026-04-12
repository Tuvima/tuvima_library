---
title: "How the Library Vault Works"
summary: "See how the Vault surfaces review work, diagnostics, and management actions across your library."
audience: "user"
category: "explanation"
product_area: "vault"
tags:
  - "vault"
  - "management"
  - "review"
---

# How the Library Vault Works

The Home page and media lane pages are for browsing â€” discovering what's in your library, finding something to read or watch. The **Vault** is different. It's the command centre for managing your library: seeing what the Engine has done, what needs your attention, and taking deliberate action.

This page explains the Vault's layout, its status system, how you resolve problems, and how the People and Universes tabs fit in.

---

## What the Vault Is For

The Vault is built around oversight and control. You go there when you want to:

- See everything the Engine has ingested, and confirm it's been correctly identified
- Investigate items that need attention ("Needs Review", "Quarantined")
- Manually resolve a misidentification
- Edit metadata, trigger a re-sync, or purge an item
- Browse your library by author, series, album, or show
- Manage people and franchise groupings

Think of it as the backstage area. Everything that happens automatically (ingestion, enrichment, identification) is visible here, auditable here, and correctable here.

---

## The Four Tabs

The Vault has four tabs, each focused on a different dimension of your library.

### Media

Every file in your library. Books, audiobooks, TV episodes, films, music tracks, and comics â€” all visible in one place, filterable by media type using the chips in the pinned header.

The Media tab is where you'll spend most of your time in the Vault. It's where you see ingestion status, confirm identifications, and fix problems.

### People

Every author, narrator, director, composer, screenwriter, and illustrator connected to your library. Always Wikidata-sourced â€” you can't add people manually, because people data is maintained by Wikidata's community and updated on a 30-day refresh cycle.

Each person entry shows a photo (when available), a name, a description, role chips (what roles they appear in your library â€” Author, Director, Narrator, etc.), and library presence counts (how many Works they're connected to).

Auto-cleaned: when all media connected to a person is removed from your library, the person entry is automatically removed. No orphaned entries.

### Universes

Franchise-level groupings. A Universe entry shows its name, description, how many Series it contains, the media breakdown (3 Books, 2 Films, 1 Audiobook), and how many people are associated.

Like People, always Wikidata-sourced and auto-cleaned when all child Series are empty.

### Collections

Smart collections, personal lists, AI-generated mixes, and user-created playlists. The Collections tab is for oversight and configuration â€” enabling or disabling smart collections, adjusting thresholds, seeing what's in each collection. See the separate documentation on Collections and Playlists for the full detail.

---

## The Layout

The Vault uses a **sidebar-driven layout**:

- **Pinned header** at the top: tabs, media type chips, toolbar (search, sort, filters, view mode)
- **Collapsible sidebar** on the left: context-aware views that change depending on which media type is selected
- **Scrollable content area** on the right: the list or grid of items

The sidebar changes its content depending on the media type:
- **Books**: All / Series / Authors
- **TV**: All Shows (seasons expand inline)
- **Music**: All / Albums / Artists / Genres
- **Films**: All / Series / Directors

"Recently Added" and "Needs Review" are always pinned at the top of the sidebar, regardless of media type. They're the two views you'll check most often.

On mobile, the sidebar becomes a slide-out drawer triggered by the filter icon â€” same content, different form factor.

---

## The Status System

Every item in the Media tab has a status. Understanding what each status means helps you know what needs attention.

### Verified
The Engine has identified this item with high confidence and enriched it successfully. Title, author, Wikidata QID all confirmed. Cover art, description, and structured properties all fetched. Nothing for you to do. Shown in green.

### Provisional
The Engine couldn't match this item confidently against an external source. The file's embedded metadata is being used as-is. This is not an error â€” it's the Engine saying "I did my best, but I'm not certain."

A Provisional item will still appear correctly in your library based on its file metadata. If you later identify it manually (by picking a candidate in the detail drawer), it upgrades to Verified.

### Needs Review
Something requires your attention. Amber highlight. In the "All Media" view, Needs Review items are **extracted from the main list and pinned as an amber-highlighted group at the very top**, auto-expanded so you see them immediately.

Common reasons:
- Two candidates with similar confidence (the Engine can't pick confidently)
- Conflicting metadata fields that need a human decision
- An identification that looks wrong and was flagged by the confidence checks
- A failed enrichment that needs a manual retry

### Quarantined
A problem was detected that prevents normal processing. Red alert banner. Could be a corrupt file, a file the Engine can't read at all, or a file whose hash has changed since it was last seen (possible file corruption). Requires investigation.

### Pending
Still processing. The pipeline is actively working on this item. Normal during the first minutes after a file is ingested.

### Failed Pipeline Steps

If individual pipeline steps fail (retail identification failed, Wikidata enrichment failed), these aren't shown as a separate status. Instead, they appear as a natural-language explanation line beneath the item row in the list â€” "Retail identification could not find a match." This keeps the status system from becoming a confusing set of technical codes.

---

## The Detail Drawer

Clicking any item in the Media tab opens the detail drawer, which slides in from the right without leaving the page.

**Pinned header** (always visible):
- Cover art
- Title, creator
- Current status with pill badge
- Universe link (if assigned)

**Scrollable sections** (collapse/expand individually):

- **Sync** â€” whether metadata writeback is enabled for this item, last sync timestamp
- **Enrichment** â€” which stages have run, when they last ran, next scheduled run
- **Pipeline** â€” stage-by-stage progress indicators (Scan, Retail, Wikidata), with inline resolution panels for each stage
- **File** â€” path, size, format, fingerprint hash, media asset ID
- **Claims** â€” the full claim history for every metadata field; who said what with what confidence

**Pinned action bar** (always visible at bottom):
- **Identify** â€” re-run the identification pipeline for this item
- **Sync Now** â€” immediately write resolved metadata back to the file's embedded tags
- **Purge** â€” remove this item from the library (confirmation required)

---

## Inline Resolution

The Pipeline section of the detail drawer is where you fix misidentified items. It includes resolution panels for both the Retail stage and the Wikidata stage.

For **Retail stage**:
- If the Engine found candidates, you see them listed with confidence scores and cover thumbnails
- Pick the correct one, or search manually by title/ISBN/ASIN
- "Add Provisional" is available if you want to manually enter metadata without a retail source

For **Wikidata stage**:
- If a QID was found, you see the Wikidata entry details
- If multiple candidates were found, you pick the correct one
- If no match was found, you can search manually by title or QID, or skip Wikidata for this item

The Retail stage shows **"Unmatched"** (not a green "Completed") when only the file scanner matched and no retail provider confirmed the identity. This distinction matters â€” "Completed" means an external source confirmed the match. "Unmatched" means the file metadata is all you have.

---

## Batch Operations

When you need to act on multiple items at once:

- **Shift+click** selects a range of items
- **Ctrl+click** adds individual items to the selection
- Clicking a group header (e.g., a Series name) selects all items in that group

A **floating action bar** appears at the bottom of the screen when any items are selected. Available actions:
- **Delete** â€” removes selected items with a confirmation dialog (lists what will be deleted)
- **Sync Now** â€” triggers immediate metadata writeback for all selected items

Selection is cleared when the action bar is dismissed or when you navigate away.

---

## Configurable Columns

Each media type has a default column set appropriate for that content type. Books show Author and Series. Films show Director and Year. Music shows Artist and Album. TV shows Season and Episode.

The **column picker** (accessible from the toolbar) lets you:
- Toggle columns on and off
- Reorder columns by dragging
- Reset to the default column set

Your preferences are saved per media type in your browser's local storage. They persist across sessions without needing a server round-trip.

Column headers are clickable for sorting. An arrow indicator shows the current sort direction. Default sort is by title (alphabetical), except the "All Media" view which defaults to newest-first. Sorting is active on Title, Status, and Universe columns.

---

## Hierarchical Sub-pages

Some content types have natural hierarchy that doesn't fit well in a flat list. TV shows have seasons and episodes. Music has albums and tracks. Books have series.

Clicking on a TV show, music album, or book series navigates to a **detail sub-page** â€” a richer view with cover art header, metadata, and the hierarchical content (season accordion for TV, flat track list for music). Breadcrumb navigation returns you to the Vault list.

These sub-pages follow the same design language as the rest of the Vault, just organized for the content type's natural structure.

---

## People Tab in Practice

The People tab is read-only in the sense that you can't manually edit person data. Person data is always Wikidata-sourced, and corrections belong in Wikidata itself.

What you can do in the People tab:
- Browse all people in your library with their roles and presence counts
- Click a person to open the detail drawer with their full biography, social links, and the works they're connected to
- See which works each person appears in, grouped by role and media type
- View the "Linked Identities" section â€” if an author uses a pen name, both identities are shown as connected via Wikidata's pseudonym resolution

The 30-day refresh cycle handles keeping person data current. If a person's Wikipedia entry is updated, the next refresh picks up the changes automatically.

---

## Universes Tab in Practice

Similar to People, the Universes tab is always Wikidata-sourced. You can't manually create a Universe â€” they emerge from the metadata relationships the Engine discovers.

What you can do:
- Browse all Universes in your library with their Series counts and media breakdowns
- Click a Universe to open the detail drawer, which shows the Series list, People, and Assets
- Click a Series name in the detail drawer â€” this navigates back to the Media tab with that Series's filter applied, showing you exactly which items belong to it

The stats bar at the top of the Universes tab shows total Universe count and total Series count across your entire library at a glance.

---

For technical details about the Vault's implementation â€” component architecture, sidebar state management, column definition format, and the full detail drawer section structure â€” see the [architecture deep-dive](../architecture/settings-and-vault.md).

## Related

- [Settings Architecture and Library Vault](../architecture/settings-and-vault.md)
- [How to Resolve Items That Need Review](../guides/resolving-reviews.md)
- [Engine API Reference](../reference/api-endpoints.md)
