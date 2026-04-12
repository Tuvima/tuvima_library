---
title: "How to Resolve Items That Need Review"
summary: "Use the Library Vault to inspect low-confidence items, metadata conflicts, and review states."
audience: "user"
category: "guide"
product_area: "vault"
tags:
  - "vault"
  - "review"
  - "conflicts"
---

# How to Resolve Items That Need Review

This guide explains what "Needs Review" means, where to find those items, and how to work through them without guessing.

---

## What "Needs Review" means

When the Engine cannot make a safe automatic decision, it asks you to choose.

Common reasons:

- Retail matching found more than one plausible candidate
- Retail matching found only a weak candidate
- Wikidata returned multiple possible identities
- Wikidata returned no usable QID for a work the Engine otherwise understands
- The file metadata is too sparse or contradictory to trust on its own

The file is still safe, its metadata is still recorded, and nothing is lost. "Needs Review" means "the system stopped before guessing."

---

## Where to find review items

1. Open the Dashboard at `http://localhost:5016`.
2. Go to **Vault**.
3. Open the **Media** tab or the **Action Center**.
4. Filter by **Needs Review** if you want to focus only on unresolved items.

Items that are still review-only may not appear in the main Vault list yet. They remain visible in Review, Activity, and the Action Center until they are either resolved or pass the Vault quality gate.

---

## Open the detail drawer first

Click the item to open its detail drawer. That drawer is the best place to understand what happened.

Look at:

- **Pipeline** for stage-by-stage history
- **Claims** for every metadata source and confidence score
- **Enrichment** for current descriptions, bridge IDs, and structured fields
- **File** for raw file facts like path, size, and fingerprint

If you only do one thing before picking a candidate, read the Pipeline section.

---

## The three stages you will see

The Vault uses the same three stages everywhere:

| Stage | What it does |
|---|---|
| **Retail** | Searches and scores provider candidates using the file's metadata. |
| **Wikidata** | Resolves a canonical QID from Retail bridge identifiers. |
| **Enrichment** | Applies follow-up metadata, images, people, and relationships after identity is settled. |

Readiness labels give the plain-English summary:

- **Pending artwork**
- **Needs review**
- **Ready**

---

## Resolve the Retail stage

If Retail needs review, you will usually see one of these situations.

### Several candidates

Compare the candidates and choose the one that best matches the file's real title, creator, edition clues, and cover.

Pay special attention to:

- creator names
- series or show names
- year
- episode or track position
- cover art that obviously belongs to the same edition or release

### No good candidates

Use the manual retail search if the automatic query was too weak.

If no provider candidate is good enough, use **Add Provisional**. That starts from the file's embedded metadata or your corrected metadata instead of inventing a provider match.

### Why the Engine stopped

Retail matching is intentionally conservative now:

- scores at or above `0.90` can be auto-accepted
- scores from `0.65` to below `0.90` are treated as ambiguous review work
- scores below `0.65` are treated as too weak to trust

That stricter gate reduces false positives, which is why you may see more review items than in older builds.

---

## Resolve the Wikidata stage

If Wikidata needs review, you are deciding which identity, if any, should become canonical.

You may see:

- one or more QID candidates
- manual search
- direct QID entry
- a no-QID path when the work is real but not well represented in Wikidata

Pick the correct QID if one exists. If none of the candidates are right, do not force it.

---

## Accepting an item without a QID

Sometimes Retail finds a good practical match, but Wikidata still cannot resolve a trustworthy QID.

That outcome is valid.

If the item has:

- a trustworthy title
- a resolved media type
- settled artwork, either present or explicitly missing

then it can still remain visible in the main Vault even without a QID. In the UI this is treated as a "QID Not Found" style outcome, not as a silent failure.

---

## What "Add Provisional" means now

`Add Provisional` is best understood as a local metadata override flow.

It is useful when:

- the file's embedded metadata is mostly right
- providers do not know the item
- you want to correct the metadata before re-running identification

It does **not** bypass the Vault quality gate. The item still needs a real title, resolved media type, and settled artwork outcome before it appears in the main Vault.

---

## When to leave an item alone

If the only thing missing is artwork settlement or a background follow-up stage, you may not need to do anything. The readiness label tells you whether the item is actually blocked or simply still progressing.

Good examples of "wait, do not guess":

- the item says **Pending artwork**
- the pipeline has a good Retail match and is still working through Wikidata
- the Engine is refreshing a stale item after a scheduled sweep

---

## Scheduled retries

You do not need to resolve every uncertain item immediately.

The Engine re-checks items over time, including:

- provider data that may have improved
- Wikidata entries that may have been created or corrected
- background enrichment that may now have enough signal to finish

So if an item is obscure and the current answer is "not enough evidence yet," leaving it for a later retry is a perfectly reasonable choice.

## Related

- [How the Library Vault Works](../explanation/how-the-vault-works.md)
- [How Two-Stage Enrichment Works](../explanation/how-hydration-works.md)
- [How the Entire Pipeline Works](../explanation/how-the-pipeline-works.md)
