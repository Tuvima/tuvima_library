---
title: "Resolve Review Items"
summary: "Use Review Queue for media items that need human confirmation before Tuvima can continue."
---

# Resolve Review Items

Review Queue is the exception queue. It is only for items that are blocked, uncertain, or need human confirmation before ingestion or enrichment can continue.

Normal media corrections do not happen in a separate workspace. If you notice a bad title, artwork, artist, episode, album, book, comic, or movie match while browsing, use the edit action on that same media page or detail page. Tuvima opens the shared media editor and returns you to the same context after the change is applied.

## Open Review Queue

1. Open **Settings**.
2. Choose **Review Queue**.
3. Select an item that needs attention.
4. Use **Review** to open the shared editor in review mode.

The Review Queue explains why the item is pending, such as a low-confidence match, missing title, failed provider lookup, failed write-back, ambiguous Wikidata candidate, or missing bridge identifier.

## What Review Can Do

Depending on the item and Engine state, Review may let you:

- Confirm or correct the item identity.
- Apply a better match.
- Adjust fields that blocked ingestion.
- Dismiss an item that no longer needs attention.
- Skip universe matching when the item can continue without a Wikidata universe link.
- Retry a failed step when the underlying problem has been fixed.

Review uses the same shared editor as inline media editing, but with review-specific context and prompts.

## What Review Is Not

Review Queue is not a Review Queue, Review Queue, or all-purpose management workbench. It should not be used for routine browsing or normal media fixes. Those fixes belong inline on Read, Watch, Listen, Search, and detail surfaces.


