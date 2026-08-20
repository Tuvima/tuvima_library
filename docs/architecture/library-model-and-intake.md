---
title: "Libraries, Sources, and Intake"
summary: "Target architecture for structured and personal libraries, universal intake, folder safety, and View."
audience: "developer"
category: "architecture"
product_area: "ingestion"
---

# Libraries, Sources, and Intake

Tuvima has two library kinds and four presentation areas. These concepts are
orthogonal: a library kind controls processing policy, while an area controls
where the library appears in the product.

| Kind | Areas | Processing |
| --- | --- | --- |
| `catalogued` | Read, Watch, Listen | Local extraction followed by configured identity and metadata providers. |
| `personal` | View | Local extraction and indexing only. External identity and metadata providers are not called. |

`Photos` and `General` are not library kinds. Phone photos, short videos,
documents, artwork, home movies, audio notes, and other personal files belong
to user-created personal libraries in View. Presentation modes such as Gallery,
Mixed Gallery, Timeline, Video, Documents, Audio, and Mixed change browsing;
they do not change the underlying library kind.

## Processing flow

```text
Source
  -> Intake
  -> Destination Library
  -> Library and Source Policy
  -> Storage
  -> Index
```

An intake request records its origin, actor, and optional destination library.
Direct-to-library actions such as browser upload, drag-and-drop, mobile backup,
connected-device import, and API intake must retain the stable destination
library ID through the pipeline. A shared incoming source has no destination
hint and invokes routing rules instead.

Shared incoming currently auto-routes only to a single eligible catalogued
library. A personal/View candidate is classified but parked for review because
the lower-level incoming worker does not yet own View's local-index service.
Explicit personal browser uploads already use the dedicated targeted View
indexer and never enter catalogue ingestion.

The destination library determines the processing branch:

- Catalogued libraries run local extraction and may continue into provider
  identity, canonical reconciliation, enrichment, and structured organization.
- Personal libraries run local extraction, exact-duplicate handling, compound
  asset grouping, thumbnail or preview generation, and local search indexing.
- Direct personal uploads are indexed through View's targeted local-asset path;
  they never create catalogue chains or identity-provider jobs.
- A local-only or manual metadata policy never calls external identity or
  metadata providers.

Staging and temporary upload paths are implementation details. Normal users do
not configure or browse them.

## Libraries and sources

A library owns policy and presentation. A source represents one concrete
folder or intake endpoint. Every source has a stable ID and declares its path,
role, management mode, recursion behavior, filesystem access, writeback
override, organization participation, intake role, health, and optional device
or profile association.

One managed source may be the explicit primary destination. Array order never
selects a destination.

### Existing library source

An existing-library source may be scanned, identified, and indexed, but Tuvima
must not move, rename, tag, overwrite, or delete its files. Changing the source
to managed changes policy only; it does not mutate existing files.

### Managed source

A managed source may accept new files and participate in organization when the
source is writable and both library and source policy permit the operation.
Organization and embedded metadata writeback remain separate permissions.

Every mutating operation passes through the shared source-mutation policy gate.
Code paths must not infer permission from a path, a global option, or a UI
selection alone.

## Reorganization

Existing files are reorganized only through an explicit plan:

1. Calculate current and proposed paths without filesystem mutation.
2. Report unchanged, renamed, moved, conflicting, unresolved, blocked, and
   invalid operations.
3. Validate source containment, destination containment, collisions,
   writability, and available space.
4. Require confirmation of that exact plan.
5. Revalidate each operation immediately before execution.
6. Record progress and support cancellation and safe retry.

The planner and executor use stable library and source IDs. A change to source
policy invalidates an unexecuted plan.

## Personal items

View stores a logical local item separately from its physical files. This lets
one item represent a Live Photo, RAW/JPEG/XMP set, original plus edited version,
or another reliable compound relationship. Physical files retain checksums,
paths, sizes, timestamps, and source identity. Generated thumbnails and previews
are stored separately and never overwrite the original.

Searchable metadata includes library, owner, filename, embedded title, item
type, capture and creation dates, device, dimensions, duration, document text
when safely available, GPS or normalized place fields, and user tags.

AI-generated captions, tags, embeddings, transcription, face recognition, and
object detection are not part of the core View implementation. Future
annotations may add provenance, confidence, and model version without becoming
required for ingestion or search.

## Access

Personal libraries declare an owner and visibility: Private, Shared with
selected profiles, or Household. The same access decision is enforced for
browse, search, thumbnails, originals, downloads, uploads, and management.
Administrator access is an explicit policy rather than an assumption in the
Dashboard.

## Pre-beta cutover policy

Before beta, application databases and configuration may be reset or rebuilt to
adopt the target architecture. The Engine does not maintain compatibility
readers, dual schemas, route aliases, or data-preserving migrations for obsolete
development state. Obsolete configuration fails fast and source media is
reingested.

This development-state policy does not weaken filesystem safety. Existing
library sources and user-owned originals remain protected from unintended
moves, renames, writeback, overwrite, or deletion.

## Visual validation and intentional differences

Media Management and View were checked at 1920×1080 and at a 390×844 mobile
viewport against the product references. The implemented hierarchy retains the
reference's compact cards, status badges, area navigation, source controls,
mixed-media filters, summary rail, and mobile stacking.

The references use a permanent left rail as primary app navigation. Tuvima
intentionally keeps Read, Watch, Listen, View, and Collections in its existing
top navigation, with the established Settings or View rail beneath it. Media
Management uses a visible inner row for Overview, Incoming, Read, Watch,
Listen, and View. Empty View libraries show a truthful scan-ready state rather
than fabricated thumbnails, counts, or backup progress.
