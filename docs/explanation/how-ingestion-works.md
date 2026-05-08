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

When a new file lands in a watched folder, Tuvima Library does not trust it immediately. The Engine waits until the file is stable, reads what it can from the file itself, stages it safely on disk, and only then starts deciding whether it is ready for the current media surfaces and the organised library.

---

## The big picture

```
File appears in a watched folder
  -> settle and lock check
  -> fingerprint
  -> scan embedded metadata
  -> classify ambiguous formats
  -> move to staging
  -> Retail and Wikidata enrichment
  -> artwork settlement
  -> browse readiness gate
  -> library organisation
```

The key idea is simple: **trust must be earned**.

---

## Step 1: Detection and settling

The Engine notices a new file quickly, but it does not scan it immediately. Large files can take time to finish copying. The system waits for the file to stop changing and confirms it is no longer locked by another process.

That avoids half-read files and noisy filesystem events.

---

## Step 2: Fingerprinting

The Engine computes a SHA-256 hash from the file contents.

That fingerprint lets the system:

- recognise the same file even if it is renamed or moved
- detect duplicates
- attach later rescans to the same underlying asset

The fingerprint is the file's durable identity, not its filename.

---

## Step 3: Scanning

The correct processor opens the file and extracts embedded metadata such as:

- title
- author or creator
- year
- narrator
- series information
- embedded cover art
- file-format-specific tags

These values become metadata claims with confidence scores. A clean EPUB title is more trustworthy than a guessed filename title, and that difference matters later.

---

## Step 4: Classifying ambiguous formats

Some formats are ambiguous. An MP3 could be a song or an audiobook chapter. An MP4 could be a movie or a TV episode.

The Engine combines file signals, folder hints, and AI-assisted classification to resolve those cases. If it still cannot make a safe call, the item goes to review instead of being forced into the wrong media type.

---

## Step 5: Safe staging

Every ingested file is moved into the staging area on disk before final organisation.

Staging is the Engine's safe holding area:

- the original file is preserved while matching and enrichment run
- retries can happen without touching the organised library
- a bad or uncertain match does not immediately rewrite your final folder structure

This staging decision is separate from whether the item is visible in the main browse surfaces.

---

## Step 6: Identity matching

After scan and staging, the Engine starts the two identity stages:

- **Retail** finds practical provider candidates and bridge IDs
- **Wikidata** resolves canonical identity from those bridge IDs

Retail matching is now stricter than older documentation described:

- `>= 0.90` can be auto-accepted
- `0.65` to `< 0.90` goes to review
- `< 0.65` is treated as too weak

That stricter gate reduces false positives and improves the quality of later Wikidata resolution.

---

## Step 7: Artwork settlement

The Engine also tracks whether artwork is:

- present
- still pending
- explicitly missing

That matters because the main browse surfaces does not show an item until its artwork outcome has settled. A file can be safely staged and partly identified without being ready for the main browse surfaces yet.

---

## Step 8: main browse surfaces visibility

An item becomes visible in the main browse surfaces only after it passes the browse readiness gate:

- non-placeholder title
- resolved media type
- settled artwork outcome

If it fails that gate, it stays visible in Activity, Review, and the Review Queue until the missing piece is resolved.

This is why "the system has seen the file" and "the file is in the main browse surfaces" are no longer the same moment.

---

## Step 9: Organisation

Promotion into the organised library on disk is a later decision based on the wider confidence and organisation rules.

So there are two separate milestones:

- the item is ready enough to surface in the main browse surfaces
- the item is ready enough to be promoted into the final organised library structure

That separation makes the product more honest and reduces bad auto-moves.

---

## When things go wrong

The pipeline is designed to fail safely:

- if the file never settles, it is not processed prematurely
- if the processor cannot read it cleanly, the system falls back to weaker signals
- if Retail cannot find a safe match, the item goes to review

---

## Watching Operations

The **Library Operations** page in Settings is the admin view for this pipeline. It keeps the existing `/settings/ingestion` route but presents ingestion as operational library health rather than a raw task log.

It shows real application state from the Engine:

- active scans and ingestion batches
- registered, provisional, and review lifecycle counts
- recent batches and their registered/review/failed outcomes
- Watch, Listen, and Read source folders from `config/libraries.json`
- provider health without exposing secrets
- pipeline counts from durable identity jobs and ingestion logs
- grouped review reasons from pending Review Queue records

While work is active, the Dashboard updates from SignalR `BatchProgress` and `IngestionProgress` events and polls the operations snapshot more frequently. When idle, it polls less often. If a signal is not tracked yet, the page says so instead of inventing a count.
- if Wikidata finds no QID, the item can still remain usable without forcing a bad identity
- if artwork is still unresolved, the item stays out of the main browse surfaces until that question is settled

## Related

- [How the Entire Pipeline Works](how-the-pipeline-works.md)
- [How Two-Stage Enrichment Works](how-hydration-works.md)
- [How to Add Media to Your Library](../guides/adding-media.md)
- [Ingestion Pipeline](../architecture/ingestion-pipeline.md)

