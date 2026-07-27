# Requested Scenario Audit

Verified against the retained clean database on 2026-07-27 after the full
database, managed-library, and watch-folder reset.

## Full ingestion

- 111 fixtures were seeded across books, audiobooks, comics, movies, music, and TV.
- 111/111 files were registered and consolidated into 108 library items.
- 49,901 metadata claims were recorded.
- The identity queue reached zero active jobs and remained stable.
- The run reported 82 identified items, 26 review outcomes, and zero failed items.

## Music

Every audited album resolves through the canonical music-album detail composer,
uses the shared `Overview` / `Details` navigation, exposes an `Artists` credit
group, and serves its managed cover with HTTP 200.

| Album | Owned | Known total | Missing | Cover |
| --- | ---: | ---: | ---: | --- |
| David Bowie — Heroes | 1 | 10 | 9 | HTTP 200 |
| The Beatles — Abbey Road | 2 | 17 | 15 | HTTP 200 |
| Kendrick Lamar — DAMN. | 1 | 14 | 13 | HTTP 200 |
| Hans Zimmer — Interstellar | 2 | 16 | 14 | HTTP 200 |

All 14 retained music-album manifests have a scalar `track_count` equal to the
number of tracks in their stored manifest. Mismatch count: **0**.

## Cross-media collections

| Collection | Automatically linked media | Owned files | Shared parent |
| --- | --- | ---: | --- |
| The Expanse | Audiobooks, Books, TV | 5 | Yes |
| Dune | Audiobooks, Books, Movies | 4 | Yes |
| Batman | Comics, Movies | 6 | Yes |

All three parent collections resolve through the canonical collection detail
composer with `Overview` / `Details` navigation and no collection-level shelf
credits.

## People and contributor identity

The same canonical person ID supplies each row below. Presentation roles are
filtered by media type, so tag aliases no longer create a second inappropriate
role such as audiobook `Artist` or music `Author`.

| Person | Media and role | Owned works |
| --- | --- | --- |
| Stephen King | Books — Author | The Long Walk; The Shining; The Talisman |
| Stephen King | Audiobooks — Author | The Shining (Unabridged) |
| James S. A. Corey | Books — Author | Caliban's War; Leviathan Wakes |
| James S. A. Corey | Audiobooks — Author | The Expanse |
| David Bowie | Music — Artist | Heroes; Five Years |
| Andy Serkis | Audiobooks — Narrator | The Hobbit |
| Andy Serkis | Movies — Actor | Rise of the Planet of the Apes |
| Bryan Cranston | Movies — Actor | Drive |
| Bryan Cranston | TV — Actor / Director | Breaking Bad |

## Canonical outward detail routes

- 6/6 audiobook-series links returned HTTP 200.
- 5/5 book-series links returned HTTP 200.
- 14/14 music-album links returned HTTP 200.
- 6/6 TV-show links returned HTTP 200.
- Audiobook-series details use exactly `Overview` / `Details`, with zero
  contributor groups and zero preview contributors.
- Standard cross-media collection details use exactly `Overview` / `Details`,
  with zero collection-level contributor groups.

## Text encoding

The exact `Alien Director Â· 1979` signature is UTF-8 text decoded once as a
Windows single-byte encoding: the UTF-8 bytes for the middle dot were exposed
as `Â·`. Historical source resources contained the same signature, confirming
the encoding-boundary cause rather than bad Alien metadata.

The current display separator is a real Unicode middle dot, ingestion repairs
known mojibake before canonical storage, and the UI source guard rejects common
double-decoding signatures. A read-only scan of all 571 declared text columns
in the retained database found **0** precise mojibake matches, including
`Â·`, broken UTF-8 punctuation/arrows, non-breaking-space corruption, and the
Unicode replacement character.

## Automated verification

- Solution tests: **2,458 passed**, **0 failed**, **36 skipped** live-provider tests.
- Solution build: **0 warnings**, **0 errors**.
- `git diff --check`: passed.
- Engine health: HTTP 200 at `http://localhost:61495/health`.

## Full-run report note

The full HTML report is the immutable snapshot taken as the destructive run
finished. It correctly records the fixture, cross-media, and person results,
but it also records two transient validation defects found during this work:

1. Interstellar was audited before the post-run album-manifest repair sweep
   supplied its cover and 16-track manifest.
2. The new route guard reused one raw group ID for multiple system-view rows.
   The guard now uses each row's deterministic outward ID.

The live results above were collected after both defects were fixed and are the
authoritative requested-scenario verification for the retained database.
