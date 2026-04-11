---
title: "How Universes and Series Work"
summary: "Understand the grouping model that connects books, films, audio, and related media."
audience: "user"
category: "explanation"
product_area: "concepts"
tags:
  - "universes"
  - "series"
  - "grouping"
---

# How Universes and Series Work

Most media software organizes by format: books in one app, movies in another, music somewhere else. Tuvima Library organizes by *story*. The same creative world â€” whether you own it as a novel, a film, an audiobook, a graphic novel, or a music soundtrack â€” lives together in one place. That place is a **Universe**.

This page explains how the grouping model works, what the terminology means, and how the Engine figures out which items belong together.

---

## The Idea Behind Universes

Consider Dune. You might own Frank Herbert's original novels as EPUBs. You might have Denis Villeneuve's film adaptations as MKVs. You might have the audiobook narrations as M4Bs. You might have the graphic novel adaptations as CBZ files. These are all separate files in separate formats. A folder-based organizer would scatter them across Books, Videos, Audio, and Comics sections with no indication they're related. A Universe brings them forward as one creative world â€” exactly as they belong.

This is the **Presentation** philosophy at the core of Tuvima Library. The stories already exist on your hard drive, fragmented. The Library's job is to find them, understand them, and surface the result as something coherent.

---

## The Hierarchy

Every item in your library lives somewhere in this structure:

```
Library
  â””â”€â”€ Universe   (the creative world â€” e.g. "Dune")
        â””â”€â”€ Series   (a sub-grouping â€” e.g. "Dune Novels" or "Dune Films")
              â””â”€â”€ Work   (a single title â€” e.g. "Dune Part One")
                    â””â”€â”€ Edition   (a specific version â€” e.g. "4K HDR Blu-ray Remux")
                          â””â”€â”€ Media Asset   (the file on disk)
```

Each level serves a different purpose:

- **Universe** â€” the franchise or creative world. Groups everything that belongs to the same story across all formats.
- **Series** â€” a sub-grouping within a Universe. Organizes related works into meaningful collections (the novels as one Series, the films as another).
- **Work** â€” a single title. "Dune Part One" is one Work, regardless of how many files or formats you own.
- **Edition** â€” a specific version of a Work. The standard theatrical cut and the director's cut are two Editions of the same Work.
- **Media Asset** â€” the actual file on disk. One Edition might have multiple Assets (e.g., the video file and its external subtitle files).

---

## How Grouping Happens Automatically

Universes and Series are resolved automatically at metadata-scoring time. The Engine doesn't need you to manually assign items to groups â€” it figures out the relationships from the metadata.

The primary signals are Wikidata relationship properties:

- **P8345 (media franchise)** â€” directly identifies the franchise a work belongs to
- **P179 (part of series)** â€” links a work to its series
- **P361 (part of)** â€” broader membership (e.g., a spin-off that's part of a larger franchise)

When the Engine successfully identifies a book and fetches its Wikidata properties, it gets these relationship values as QIDs (Wikidata identifiers). It then looks up those QIDs to find their labels. Two items that share the same franchise QID belong in the same Universe.

Secondary signals also contribute: shared author, shared narrative roots, shared characters detected by the Universe Graph. Shared author alone doesn't necessarily create a Universe â€” an author might write in completely unrelated genres â€” but combined with shared series membership or franchise identifiers, the grouping becomes clear.

**Importantly, Universes and Series have no presence on the filesystem.** Your files are organized by media type and title. The Universe grouping exists only in the data store and is resolved at query time. This means the grouping can change as the Engine learns more â€” a standalone novel might later be recognized as part of a franchise when new metadata arrives.

---

## Series Are Flexible Containers

A Series is not limited to numbered sequences. It's any meaningful grouping of related Works.

Some Series are obvious sequential collections: the Dune novels in publication order, the MCU films in release order, the Discworld subseries by character arc. But Series can also be:

- **Adaptation clusters**: all film adaptations of a specific novel
- **Spin-off works**: tie-in novellas and short stories connected to a main series
- **Thematic collections**: all standalone novels by an author set in the same fictional universe
- **Cross-media narratives**: the audiobook version of a series in one Series alongside the ebook version in another

What defines a Series is **shared contextual metadata** â€” the same Wikidata series QID, the same franchise membership, the same author+universe combination. Not a shared file format, not a shared folder location.

---

## Universes Are Optional

Not everything belongs to a franchise. A standalone novel with no series membership, no adaptations, and no franchise connections lives directly under the Library â€” no Universe wrapper needed.

Only when the Engine discovers franchise-level relationships does it promote related Series under a common Universe. A Series that belongs to no larger creative world appears at the top level on its own.

This also means the grouping can evolve. A standalone Series might later be recognized as part of a Universe when the Engine identifies a film adaptation in your collection, or when Wikidata gains new relationship data about that franchise.

---

## The Terminology Explained

You'll notice the user-facing names and the internal code names are different. This is intentional â€” it decouples the user experience from the implementation, so the code can evolve without changing the product language.

| What you see in the Dashboard | What the code calls it | Why different |
|---|---|---|
| Universe | ParentHub | The code predates the Universe concept; renaming internally would risk data store migrations |
| Series | Hub | Same history |
| Work | Work | Same in both |
| Edition | Edition | Same in both |
| Media Asset | MediaAsset | Minor formatting difference only |

When reading code or architecture documentation, Hub = Series and ParentHub = Universe. When writing anything user-facing â€” UI labels, documentation, error messages â€” always use Universe and Series.

---

## The Universe Graph: Beyond Simple Grouping

Grouping items together is the first layer. The Universe Graph is the second â€” a richer map of relationships *within* and *across* Universes.

The Universe Graph tracks:

- **Characters** â€” fictional entities that appear across multiple works (a character appearing in both novels and their film adaptations)
- **Locations** â€” fictional places that recur across media
- **Factions** â€” organizations, families, orders
- **Narrative relationships** â€” which works are sequels, prequels, spin-offs, adaptations of each other

This graph powers features beyond simple browsing. It's what enables "which actor played this character in which adaptation?" It's what connects a graphic novel adaptation to the novel that inspired it. It's the infrastructure for the Chronicle Explorer â€” the visual graph at `/universe/{QID}/explore` where you can navigate these relationships interactively.

For now, Universe Graph data comes from Wikidata properties. The Tuvima.Wikidata.Graph module handles in-memory graph queries over this data so you can ask relationship questions without a network connection.

---

For technical details about the Universe Graph schema, the relationship model, SPARQL query patterns, and the Chronicle Engine's temporal qualifier system â€” see the [architecture deep-dive](../architecture/universe-graph.md).

## Related

- [Universe Graph](../architecture/universe-graph.md)
- [Glossary](../reference/glossary.md)
- [Your First Library](../tutorials/first-library.md)
