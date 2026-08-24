---
title: "View Personal Media Architecture"
summary: "Ownership, permissions, scopes, Galleries, queries, and extension boundaries for Tuvima View."
audience: "developer"
category: "architecture"
product_area: "view"
tags:
  - "view"
  - "personal-media"
  - "privacy"
  - "galleries"
---

# View Personal Media Architecture

View is Tuvima's local-first home for personal photos, short videos,
documents, and audio. It is not a catalogue lane and never sends local-only or
manual media through retail metadata, canonical identity, Wikidata, or Review
Queue workflows.

The product presents one **Personal Space** per enabled profile. A Personal
Space may use several sources or devices, but those sources describe where
files came from rather than creating separate user-facing photo libraries.
Existing library and source identifiers remain useful implementation details.

## Identity and ownership

Every personal asset resolves an owning profile, Personal Space/library,
source or device when known, and one or more physical files. Compound media
such as Live Photos and RAW/JPEG/XMP groups remain one logical asset.

Identical hashes do not merge logical ownership. Two profiles may independently
own the same bytes, retain distinct source paths, apply different flags, and
place the asset in different Galleries. Generated derivatives are cache entries
and never replace or modify originals.

## Permissions

Each profile has independent View capabilities:

- **View enabled** provisions and exposes its Personal Space.
- **Access Shared View** allows browsing the authorized shared aggregation.
- **Include in Shared View** allows eligible assets from its Personal Space to
  contribute to that aggregation.
- **Share Galleries** allows sharing Galleries with selected profiles.

Access and inclusion are deliberately independent. Administrators manage these
capabilities, but administrative configuration rights do not silently become a
normal profile's content-browsing rights.

The Engine derives the active profile from trusted request identity. A browser
supplied profile, library, Gallery, or asset identifier is only a requested
resource and never proof of access.

## View scopes

View supports three typed scopes:

- `Shared` resolves the contributing Personal Spaces the active profile may
  access.
- `Mine` resolves the active profile's Personal Space.
- `Profile(profileId)` resolves another profile only when explicitly permitted.

The scope resolver returns authorized internal Personal Space/library IDs. All
timeline, search, derivative, original, Gallery, People, Places, and Collection
queries consume this resolved scope rather than accepting arbitrary library ID
lists.

The last selected scope is stored server-side per profile. First use defaults to
Shared when permitted and Mine otherwise. A saved scope that becomes
unauthorized falls back to Shared when still permitted, then Mine.

## Asset states

Favorite, Hidden, Archive, and Trash are logical states. Archive removes an
asset from the normal Photos timeline. Trash is recoverable and records when the
asset was trashed. Neither operation mutates an original file. Permanent file
deletion is a separate explicit operation governed by source containment,
management mode, access mode, and filesystem mutation policy.

## Timeline and search

The main Photos timeline is date grouped and uses captured time when available,
then a stable import/creation fallback. It uses cursor pagination ordered by the
effective date plus stable asset ID. Deep `OFFSET` pagination and loading an
entire library for client-side grouping are not supported target behavior.

Metadata search uses the local FTS index for filenames, titles, dates, device,
place, tags, media kind, and indexed document text. The query boundary permits
future semantic results to be blended in, but semantic processing is never a
prerequisite for ordinary metadata search.

## Galleries

A Gallery is a lightweight profile-owned personal-media container.

- A **Manual Gallery** stores unique asset memberships and may support explicit
  ordering and a selected cover.
- A **Smart Gallery** stores a typed rule definition and evaluates it against
  the viewer's authorized asset scope. It does not accept manual membership.

Gallery removal never removes an asset. Gallery deletion never deletes media.
Sharing is Private or selected-profile access; authorization is re-evaluated for
details, counts, cover previews, and every returned asset.

Smart Gallery and Collection rules use one versioned rule core with separate
field registries. Personal-media rule fields are capability-driven so future
processors can add people, objects, scenes, OCR, captions, or embeddings without
inventing a parallel rule format.

## Collections integration

Collections may reference an entire Manual or Smart Gallery, or contain their
own saved View rule definition. They do not manually contain individual local
asset IDs. Gallery references remain references rather than copied membership
rows, so later Gallery changes are reflected naturally.

Collection projections always reapply View authorization. Private thumbnails,
filenames, counts, people, and locations must not leak through Collection
summaries or previews.

## View surfaces

Ordinary View routes use the shared section shell and exactly four primary
destinations:

- `/view` — Photos
- `/view/galleries` — Galleries
- `/view/people` — People
- `/view/places` — Places

Shared View, Favorites, Videos, Archive, Recently Added, Hidden, and Trash are
scopes or filters in the content area. Sources, devices, uploads, backup,
duplicates, storage, AI configuration, and View administration remain in
Settings. Immersive asset, Gallery, person, and place details may intentionally
omit the section rail.

## People, Places, and AI

Places works from extracted GPS metadata and does not require AI. Map rendering
must have an accessible list fallback and must not make an unconfigured
third-party tile request.

People consumes provenance-aware local annotations when a real processor has
produced them. When processing is unavailable or incomplete, the UI presents a
truthful capability state. Face recognition, semantic search, OCR, object
detection, captions, and embeddings run asynchronously through the existing
Local AI architecture and never block ingestion or asset availability.

## Scale and safety

View queries are designed for at least hundreds of thousands of assets. Common
filters use indexed owner/space, effective date, media kind, flags, location,
hash, Gallery membership, tag, and annotation fields. Query-plan checks and
large seeded datasets are part of release verification.

Existing and read-only sources remain immutable. Personal-media work must not
weaken path containment or source mutation policy. The pre-beta reset rule
allows obsolete application database and configuration state to be rebuilt; it
does not authorize modifying or deleting user-owned originals.
