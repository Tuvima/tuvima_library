# Supported Media Types and Formats

Seven media types are supported. Each type has a dedicated processor, a set of supported file extensions, and a configured set of providers. Ambiguous formats (MP3, MP4) are resolved via heuristics and AI classification.

---

## Books (EPUB)

**Processor:** `EpubProcessor` — priority 100

### Supported formats

| Extension | Format |
|---|---|
| `.epub` | Electronic Publication (EPUB 2 and EPUB 3) |
| `.pdf` | Portable Document Format |

### Extracted metadata

| Field | Confidence | Notes |
|---|---|---|
| `title` | 0.95 | From OPF `<dc:title>` |
| `author` | 0.95 | From OPF `<dc:creator>` |
| `publisher` | 0.95 | From OPF `<dc:publisher>` |
| `language` | 0.95 | From OPF `<dc:language>` (BCP-47) |
| `description` | 0.95 | From OPF `<dc:description>` |
| `date` | 0.95 | From OPF `<dc:date>` |
| `isbn` | 1.0 | From OPF `<dc:identifier>` where scheme = ISBN |
| `word_count` | 1.0 | Computed from content documents |
| `cover_image` | — | Extracted from EPUB manifest cover item |

### Providers

Waterfall order during Stage 1:

1. Open Library — ISBN-first lookup, description and cover
2. Google Books — ISBN-first, falls back to title+author search
3. Apple API — title+author search, cover art and ratings
4. Wikidata — Stage 2, QID resolution via ISBN bridge

### Ambiguity resolution

EPUB and PDF are unambiguous. No classification required.

### Organization template

```
Books/{Title} ({Qid})/{Title}.epub
```

---

## Audiobooks (M4B, MP3)

**Processor:** `AudioProcessor` — priority 95

### Supported formats

| Extension | Format | Ambiguity |
|---|---|---|
| `.m4b` | MPEG-4 Audio Book | Unambiguous — always audiobook |
| `.mp3` | MPEG Audio Layer 3 | Ambiguous — may be music or audiobook |

### Extracted metadata

| Field | Confidence | Notes |
|---|---|---|
| `title` | 0.90 | From ID3 `TIT2` or M4B `©nam` tag |
| `author` | 0.90 | From ID3 `TPE1` or M4B `©ART` tag |
| `narrator` | 0.85 | From ID3 `TPE3` or M4B `©wrt` tag |
| `album` | 0.90 | Series name often embedded here |
| `year` | 0.90 | From ID3 `TDRC` |
| `genre` | 0.80 | From ID3 `TCON` |
| `track_number` | 0.95 | From ID3 `TRCK` — used for multi-part audiobooks |
| `duration_sec` | 1.0 | Computed from audio stream |
| `container` | 1.0 | `m4b` or `mp3` |
| `audio_bitrate` | 1.0 | From audio stream properties |
| `asin` | 1.0 | From ID3 custom tag `TXXX:ASIN` — used as retail bridge ID |
| `cover_image` | — | From embedded ID3 `APIC` frame |

### Ambiguity resolution for MP3

Classification order for MP3 files:

1. `TXXX:ASIN` tag present → audiobook
2. Genre tag contains "Audiobook", "Spoken Word", or "Podcast" → classified accordingly
3. `TPE3` (narrator) tag present → audiobook
4. Duration > 20 minutes → audiobook candidate
5. AI `MediaTypeAdvisor` — final classification using all available signals

### Providers

1. Apple API — ASIN-first lookup for Audible content; cover art, narrator, series data
2. Wikidata — Stage 2, QID resolution

### Organization template

```
Audiobooks/{Title} ({Qid})/{Title}.m4b
```

---

## Movies

**Processor:** `VideoProcessor` — priority 90

### Supported formats

| Extension | Format | Ambiguity |
|---|---|---|
| `.mkv` | Matroska Video | Ambiguous — may be movie or TV |
| `.mp4` | MPEG-4 Video | Ambiguous — may be movie, TV, or other |
| `.m4v` | iTunes Video | Ambiguous |
| `.webm` | WebM Video | Ambiguous |
| `.avi` | Audio Video Interleave | Ambiguous |

### Extracted metadata

| Field | Confidence | Notes |
|---|---|---|
| `title` | 0.75 | From filename parsing (lower confidence — filenames vary widely) |
| `container` | 1.0 | `mkv`, `mp4`, etc. |
| `video_width` | 1.0 | From video stream |
| `video_height` | 1.0 | From video stream |
| `duration_sec` | 1.0 | From container |
| `video_codec` | 1.0 | e.g., `H.264`, `HEVC`, `AV1` |
| `frame_rate` | 1.0 | From video stream |

### Ambiguity resolution for video files

Classification order:

1. Filename matches `SxxExx` or `1x01` season/episode pattern → TV
2. Filename contains year in parentheses `(2024)` and no episode markers → Movie candidate
3. AI `MediaTypeAdvisor` with filename and duration as inputs
4. Promotion held at `NeedsReview` if classification confidence < threshold

### Providers

1. TMDB — title+year search; cover art (poster), description, ratings, cast, TMDB ID bridge
2. Wikidata — Stage 2, QID resolution via TMDB ID bridge

### Organization template

```
Movies/{Title} ({Qid})/{Title}.mkv
```

---

## TV Shows

**Processor:** `VideoProcessor` — priority 90

### Supported formats

Same as Movies. Classified as TV via filename pattern matching.

### Extracted metadata

| Field | Confidence | Notes |
|---|---|---|
| `series_title` | 0.70 | Parsed from filename before season/episode marker |
| `season_number` | 0.95 | Parsed from `SxxExx`, `Season xx`, or `xx x xx` patterns |
| `episode_number` | 0.95 | Parsed from filename |
| `episode_title` | 0.70 | Parsed from filename after episode marker (when present) |
| `container` | 1.0 | |
| `video_width`, `video_height` | 1.0 | |
| `duration_sec` | 1.0 | |
| `video_codec` | 1.0 | |
| `frame_rate` | 1.0 | |

### Filesystem conventions

TV follows the Plex naming convention for maximum compatibility:

```
TV/{Series} ({Qid})/Season {Season}/S{Season}E{Episode} - {Title}.mkv
```

Example:
```
TV/Breaking Bad (Q1079331)/Season 01/S01E01 - Pilot.mkv
```

### Providers

1. TMDB — series and episode metadata; poster and backdrop images
2. Wikidata — Stage 2, QID resolution

---

## Music

**Processor:** `AudioProcessor` — priority 95

### Supported formats

| Extension | Format | Ambiguity |
|---|---|---|
| `.flac` | Free Lossless Audio Codec | Unambiguous — always music |
| `.ogg` | Ogg Vorbis | Unambiguous — always music |
| `.wav` | Waveform Audio | Unambiguous — always music |
| `.mp3` | MPEG Audio Layer 3 | Ambiguous — see audiobooks |
| `.m4a` | MPEG-4 Audio | Ambiguous — may be music or audiobook |

### Extracted metadata

| Field | Confidence | Notes |
|---|---|---|
| `title` | 0.95 | From ID3 `TIT2` or equivalent |
| `artist` | 0.95 | From ID3 `TPE1` |
| `album_artist` | 0.95 | From ID3 `TPE2` |
| `album` | 0.95 | From ID3 `TALB` |
| `year` | 0.90 | From ID3 `TDRC` |
| `track_number` | 0.95 | From ID3 `TRCK` |
| `disc_number` | 0.95 | From ID3 `TPOS` |
| `genre` | 0.85 | From ID3 `TCON` |
| `duration_sec` | 1.0 | |
| `cover_image` | — | From embedded `APIC` frame |
| `musicbrainz_recording_id` | 1.0 | From `TXXX:MusicBrainz Track Id` — used as bridge ID |
| `musicbrainz_album_id` | 1.0 | From `TXXX:MusicBrainz Album Id` |

### Ambiguity resolution for MP3/M4A

Classification order:

1. MusicBrainz tags present → music
2. `track_number` present and duration < 15 minutes → music candidate
3. Genre tag is not "Audiobook", "Spoken Word", or "Podcast" → music candidate
4. AI `MediaTypeAdvisor` — final arbiter

### Providers

1. MusicBrainz — recording and release lookup via MBID bridge; track, album, and artist metadata
2. Wikidata — Stage 2, QID resolution

### Organization template

Follows Plex Music naming convention:

```
Music/{Artist}/{Album} ({Qid})/{TrackNumber} - {Title}.flac
```

Example:
```
Music/Pink Floyd/The Dark Side of the Moon (Q183221)/01 - Speak to Me.flac
```

---

## Comics

**Processor:** `ComicProcessor` — priority 85

### Supported formats

| Extension | Format |
|---|---|
| `.cbz` | Comic Book Archive (ZIP) |
| `.cbr` | Comic Book Archive (RAR) |

### Extracted metadata

**From archive structure:**

| Field | Confidence | Notes |
|---|---|---|
| `page_count` | 1.0 | Count of image files in archive |

**From ComicInfo.xml (when present):**

| Field | Confidence | Notes |
|---|---|---|
| `title` | 0.95 | `<Title>` element |
| `series` | 0.90 | `<Series>` element |
| `issue_number` | 0.95 | `<Number>` element |
| `volume` | 0.90 | `<Volume>` element |
| `summary` | 0.90 | `<Summary>` element |
| `year` | 0.90 | `<Year>` element |
| `publisher` | 0.90 | `<Publisher>` element |
| `writer` | 0.90 | `<Writer>` element |
| `penciller` | 0.90 | `<Penciller>` element |
| `colorist` | 0.90 | `<Colorist>` element |
| `genre` | 0.85 | `<Genre>` element |
| `language_iso` | 0.95 | `<LanguageISO>` element |

ComicInfo.xml is the de facto standard metadata format for CBZ/CBR archives. Not all archives include it; files without it fall back to filename parsing.

### Providers

1. Metron (Comic Vine) — issue search by series+number; publisher, creator, and story arc data
2. Wikidata — Stage 2, QID resolution for notable series

### Organization template

```
Comics/{Title} ({Qid})/{Title}.cbz
```

---

## Podcasts

**Processor:** `AudioProcessor` — priority 95

### Supported formats

| Extension | Format |
|---|---|
| `.mp3` | MPEG Audio Layer 3 |
| `.m4a` | MPEG-4 Audio |

### Extracted metadata

| Field | Confidence | Notes |
|---|---|---|
| `title` | 0.90 | Episode title from ID3 `TIT2` |
| `podcast_title` | 0.90 | Show name from ID3 `TALB` |
| `episode_number` | 0.85 | From ID3 `TRCK` |
| `description` | 0.85 | From ID3 `COMM` |
| `year` | 0.90 | From ID3 `TDRC` |
| `duration_sec` | 1.0 | |
| `cover_image` | — | From embedded `APIC` frame |

Podcast classification relies on genre tag ("Podcast") or manual Library Folder configuration specifying `"media_types": ["Podcasts"]`.

### Providers

1. Apple Podcasts — show and episode lookup; description, cover art, ratings
2. Podcast Index — open podcast database; additional episode metadata
3. Wikidata — Stage 2, QID resolution for notable shows

### Organization template

```
Podcasts/{Title} ({Qid})/{Title}.mp3
```

---

## Ambiguous Format Summary

| Extension | Default Classification | Resolution Method |
|---|---|---|
| `.mp3` | Uncertain | Heuristics → AI `MediaTypeAdvisor` |
| `.mp4` | Uncertain | Filename pattern → AI `MediaTypeAdvisor` |
| `.m4a` | Uncertain | Heuristics → AI `MediaTypeAdvisor` |
| `.m4v` | Movie | Filename pattern → AI confirmation |
| `.mkv` | Movie or TV | Filename pattern → AI confirmation |
| `.avi` | Movie or TV | Filename pattern → AI confirmation |
| `.webm` | Movie or TV | Filename pattern → AI confirmation |

All other extensions (`.epub`, `.pdf`, `.m4b`, `.flac`, `.ogg`, `.wav`, `.cbz`, `.cbr`) are unambiguous and require no AI classification.
