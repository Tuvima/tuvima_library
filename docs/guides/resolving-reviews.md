# How to Resolve Items That Need Review

This guide explains what "Needs Review" means, where to find those items, and how to work through them one by one or in bulk.

---

## What "Needs Review" means

When the Engine processes a file, it tries to identify it with enough confidence to place it in your library automatically. Sometimes it can't reach that confidence — perhaps the file had sparse metadata, an unusual filename, or no match in any provider. When that happens, the Engine puts the item in a "Needs Review" state and surfaces it for you to inspect and correct.

Items in this state are not lost. The file is safe, the data the Engine extracted is recorded, and everything the Engine learned during its attempts is still available for you to see. You're simply being asked to confirm or guide the final outcome.

---

## Finding items that need review

1. Open the Dashboard at `http://localhost:5016`.
2. Click **Vault** in the left navigation.
3. The **Media** tab opens by default.
4. Items with a "Needs Review" status are automatically pinned to the top of the list, highlighted in amber. They form a separate group so you can find them at a glance without scrolling through your whole library.

You can also filter by status using the toolbar at the top of the Vault — click the status filter and select "Needs Review" to see only those items.

---

## Opening the detail drawer

Click any item in the list to open its detail drawer on the right side of the screen. The drawer has three sections across the top:

- **Cover art** — whatever the Engine found, or a placeholder if nothing was available.
- **Title and status** — the current working title and the "Needs Review" status pill.
- **Universe link** — if the Engine found a franchise connection, it appears here.

Below that, collapsible sections give you a complete picture of what the Engine knows:

- **Pipeline** — the history of each processing stage and what happened at each one.
- **Claims** — every piece of metadata the Engine found, which source it came from, and how confident each one was.
- **Sync** — file writeback settings.
- **Enrichment** — provider-level results.
- **File** — raw file information (path, size, format, fingerprint).

The most important section for resolving reviews is **Pipeline**, which contains inline resolution panels for both enrichment stages.

---

## Understanding pipeline stages

Every file passes through three stages, and you can see the outcome of each one in the Pipeline section:

| Stage | What it does |
|---|---|
| **Scan** | Opens the file and reads embedded metadata (title, author, cover art, tags). |
| **Retail** | Queries retail providers (Apple, Google Books, TMDB, and others) to gather cover art, descriptions, ratings, and identifiers like ISBNs or TMDB IDs. |
| **Wikidata** | Uses the identifiers gathered in Retail to find a precise Wikidata entry, then fetches canonical structured data — author, series, genre, people, and more. |

Each stage shows a coloured indicator:
- **Green** — the stage completed successfully.
- **Amber** — the stage ran but the result is uncertain or incomplete.
- **Grey** — the stage hasn't run yet or was skipped.

Hover over any stage indicator to read a plain-English description of what happened.

---

## Resolving the Retail stage

If the Retail stage shows as unresolved, expand the **Pipeline** section and find the Retail resolution panel.

You will see one of three situations:

**Candidates available** — The Engine found several possible matches from retail providers and is asking you to confirm which one is correct. Each candidate shows a cover image, title, author, year, and the provider it came from. Click the correct one to accept it.

**No candidates** — The Engine couldn't find a match at all. You can:
- Type a title or author into the **search box** and search manually. Results from retail providers appear in real time.
- Click **Add Provisional** to tell the Engine to use the metadata already extracted from the file. A form pre-populated with the file's embedded data opens — you can correct any field before saving.

**"Unmatched"** at the Retail stage means only the file scanner found data — no retail provider confirmed the item. This is different from an error; the Engine has the file's embedded metadata and can proceed, but it's flagging that the retail confirmation step didn't produce a result.

---

## Resolving the Wikidata stage

If the Wikidata stage shows as unresolved, the Engine either found multiple possible Wikidata entries (QIDs) for this item or none at all.

Expand the Wikidata resolution panel. You will see:

**Candidates available** — A ranked list of Wikidata entries the Engine considers possible matches. Each shows the entity name, its Wikidata description, and any identifying information. Click the correct one to accept it.

**No candidates** — Click **Search Wikidata** and type a title or author. A live search against Wikidata returns results. Pick the correct entry.

**Skip universe matching** — If you're confident the item exists but has no Wikidata entry, or if you simply want to move on, you can accept the file as-is without a Wikidata match. The item will be given Provisional status.

Once you accept a Wikidata entry, the Engine fetches the full structured data for that entry in the background and updates the item.

---

## Provisional status explained

An item is **Provisional** when the Engine has done what it can but couldn't confirm identity through a retail provider or Wikidata. In this state:

- The file's embedded metadata is treated as the authority for all fields.
- Any corrections you make in the detail drawer are recorded and will influence future identification attempts.
- The item is included in the 30-day refresh cycle — the Engine will try again automatically, and if new data becomes available or providers return different results, the item may resolve on its own.

Provisional items are fully usable. They appear in your library, can be organised into Series and Universes, and can be enriched manually at any time.

---

## Resolving items in bulk

If you have many items to review, you can select and act on them together.

- **Shift+click** to select a range of items in the list.
- **Ctrl+click** to add or remove individual items from the selection.
- Click the **group header checkbox** to select everything in the "Needs Review" group at once.

When items are selected, a floating action bar appears at the bottom of the screen with the following options:

- **Sync Now** — re-runs enrichment for all selected items immediately, without waiting for the scheduled refresh cycle.
- **Delete** — removes selected items from your library (with a confirmation dialog before anything is deleted).

---

## The 30-day refresh cycle

You don't have to resolve everything manually right away. The Engine automatically re-runs enrichment on all items every 30 days. This means:

- If a provider adds new data for a title that wasn't in their catalogue last month, the Engine will pick it up.
- If Wikidata adds an entry for a book or film that was previously unmatchable, the Engine will find it.
- Items that were Provisional may resolve automatically over time as more data becomes available.

You can also trigger a manual refresh at any time by selecting an item (or multiple items) and clicking **Sync Now** in the floating action bar, or by clicking **Sync Now** in the detail drawer's action bar at the bottom.
