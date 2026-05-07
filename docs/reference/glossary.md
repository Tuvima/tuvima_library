---
title: "Glossary"
summary: "Reference the core product and architecture terms used throughout Tuvima Library documentation."
audience: "user"
category: "reference"
product_area: "concepts"
tags:
  - "glossary"
  - "terminology"
  - "concepts"
---

# Glossary

Reference definitions for common Tuvima Library terms. User-facing names are used where possible. Internal code names appear only when they help connect the docs to the codebase.

---

## C

**Canonical Value**
The winning metadata value for a field after the Priority Cascade resolves competing claims.

**Claim**
A single metadata value from a specific source with a confidence score. Claims are append-only and remain available for audit even when they do not win.

**Collection**
See **Series**. The codebase often uses `Collection` where the product surface says **Series**.

---

## D

**Dashboard**
The web UI (`MediaEngine.Web`) where you browse the library, inspect the current media surfaces, and review ingestion progress.

---

## E

**Edition**
A specific release or format of a work, such as one ebook edition, one audiobook release, or one film cut.

**Engine**
The backend application (`MediaEngine.Api`) that runs ingestion, enrichment, storage, and the API used by the Dashboard.

**Enrichment**
Follow-up metadata work that happens after basic identity is settled, such as people links, extra images, relationships, summaries, and other deeper metadata.

---

## H

**Hydration**
The two-stage identity enrichment process that runs after ingestion. Stage 1 (**Retail**) gathers practical external matches, cover art, descriptions, and bridge IDs. Stage 2 (**Wikidata**) uses those bridge IDs to resolve canonical identity and structured facts.

---

## L

**Library Folder**
A configured folder in `config/libraries.json` that tells the Engine where to scan, what media types to expect, and whether the folder is watched continuously or imported once.

---

## M

**Media Asset**
A single file on disk, such as one EPUB, one MKV, or one M4B.

**Media Type**
The resolved category for a file: Books, Audiobooks, Movies, TV, Music, or Comics.

---

## P

**Priority Cascade**
The rules that decide which metadata source wins when several disagree. User locks win first, configured field priorities come next, Wikidata is the default authority for canonical structured facts, and highest confidence wins only after those earlier rules are considered.

**Processor**
Code that opens a specific file format and extracts embedded metadata from it.

**Provider**
An external metadata source such as Apple, TMDB, MusicBrainz, Metron, or Wikidata.

---

## Q

**QID**
A Wikidata entity identifier such as `Q190804`. A QID is the strongest identity anchor when one can be resolved, but an item can still remain visible in the main browse surfaces without a QID if it passes the browse readiness gate.

**QID Not Found**
A terminal precision-preserving outcome where Retail succeeded but Wikidata could not resolve a trustworthy QID. The item remains marked at the Wikidata stage and may still be usable in the main browse surfaces if title, media type, and artwork are settled.

---

## R

**Readiness Label**
The plain-English summary shown in the current media surfaces, such as **Pending artwork**, **Needs review**, or **Ready**.

**Retail**
The first identity stage. Retail providers search external catalogues, return candidates, and supply the bridge IDs that make Wikidata resolution precise.

---

## S

**Series**
A grouping of related works, such as a book series, film series, or album grouping.

**Staging**
The safe on-disk holding area where files live between ingestion and final organisation. Staging is not the same thing as main browse surfaces visibility; an item can be staged, known to the system, and still hidden from the main browse surfaces until the quality gate is satisfied.

---

## U

**Universe**
A franchise-level grouping above Series that links related works into a wider creative world.

---

## V

**media library**
The management surface of the product. the current media surfaces shows stage progress, review state, artwork truth, and readiness. The main browse surfaces only shows items that pass the browse readiness gate.

**browse readiness gate**
The rule that controls whether an item appears in the main browse surfaces. The item must have a non-placeholder title, a resolved media type, and a settled artwork outcome.

---

## W

**Wikidata**
The canonical identity and structured-fact authority used after Retail has provided bridge IDs.

**Work**
A single title independent of format or edition.

**Writeback**
The process of writing resolved metadata back into supported file tags after enrichment or user correction.

## Related

- [How File Ingestion Works](../explanation/how-ingestion-works.md)
- [How the Entire Pipeline Works](../explanation/how-the-pipeline-works.md)
- [How the Review Queue Works](../guides/resolving-reviews.md)


