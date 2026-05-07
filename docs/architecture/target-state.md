---
title: "Target State"
summary: "Forward-looking architecture notes for features and structures that are planned but not yet implemented."
audience: "developer"
category: "architecture"
product_area: "roadmap"
status: "target-state"
tags:
  - "future"
  - "roadmap"
  - "planning"
---

# Target State

> **NONE of the features in this document are implemented.** This document describes the planned future state of Tuvima Library. It exists to preserve design decisions and prevent accidental overlap with what is already built.

Target-state work still follows the current quality gates: do not bring back the retired Vault/LibraryPage workflow, keep media correction inline through the shared editor, keep Review Queue as the exception workflow, and keep Settings/Admin focused on configuration and operations.

---

## Playback and Streaming

Four in-browser players covering every media type in the library.

### EPUB Reader

Route: `/read/{assetId}`

- Paginated CSS multi-column view
- Chapter sidebar populated from the EPUB table of contents
- Font size and family adjustment
- Dark and light reading modes
- Resume from last position
- Keyboard navigation, mobile swipe gestures

Content served via `GET /read/{assetId}/chapter/{index}` Ã¢â‚¬â€ EPUB chapter HTML/XHTML with embedded images and CSS.

### Comic Viewer

Route: `/read/{assetId}` (comic media type)

- Full-page image display
- Page-turn navigation via click, swipe, or keyboard
- Page thumbnail sidebar
- Zoom and pan for high-resolution panels
- LTR/RTL toggle for manga
- Prefetch of next 2Ã¢â‚¬â€œ3 pages

Content served via `GET /comic/{assetId}/page/{pageNum}` Ã¢â‚¬â€ individual images extracted from CBZ/CBR archives.

### Audiobook Player

Rendered as a persistent bottom bar in `MainLayout.razor` Ã¢â‚¬â€ survives page navigation. A `PlaybackStateService` (scoped per circuit, in `Services/Playback/`) manages the active audio session and exposes play/pause/seek to any component.

- Play/pause, skip Ã‚Â±30 seconds
- Playback speed (0.5xÃ¢â‚¬â€œ3x)
- Chapter list with direct navigation
- Sleep timer
- Progress bar with chapter position markers

### Video Player

Route: `/watch/{assetId}`

- HTML5 video with HLS.js for adaptive bitrate streaming
- Subtitle track selection Ã¢â‚¬â€ SRT, VTT, ASS with on-the-fly WebVTT conversion
- Chapter markers on the scrub bar
- Playback speed adjustment
- Picture-in-Picture
- Keyboard shortcuts: Space (play/pause), F (fullscreen), M (mute), arrow keys (seek)
- "Mark as watched" triggered automatically at 90% completion threshold

Content served via:
- `GET /stream/{assetId}/subtitles/{trackIndex}` Ã¢â‚¬â€ subtitle extraction from MKV, converted to WebVTT
- `GET /stream/{assetId}/chapters` Ã¢â‚¬â€ chapter metadata from MKV/M4B

### Progress Tracking

All four players share a common progress API:

- `PUT /progress/{assetId}` Ã¢â‚¬â€ upserts UserState with progress percentage, last-accessed timestamp, and media-specific extended data (page number, chapter index, video timestamp)
- `GET /progress/{assetId}` Ã¢â‚¬â€ retrieves current position for resume

Progress updates are sent at configurable intervals (default: every 30 seconds, or on chapter/page change).

---

## Authentication and Multi-User

### Local Authentication

A profile selection grid on the login page shows all configured profiles (avatar + name). Selecting a profile prompts for PIN (minimum 4 digits) or password (minimum 8 characters).

Sessions are managed via secure HTTP-only cookies (or JWT for API access). A "remember me" option persists sessions for a configurable period (default: 30 days).

All user-scoped data is bound to a `profileId`:
- Playback and reading progress (UserState)
- Reading and watching history
- Navigation configuration
- UI theme preferences

PIN and password management is available in the Users settings tab.

### Shared Journey Detection

When two or more profiles access the same asset within a 5-minute window, the Engine tags the session as a "Shared Journey." Solo sessions are tagged "Solo Journey." The Dashboard surfaces which journeys were shared.

### Parental Controls

Content maturity ratings sourced from TMDB (P1657 via Wikidata) or applied manually. Profile-level maturity filter (Kids, Teen, Adult). Access to mature-rated content requires a PIN.

### OIDC (Phase 2)

Google, Facebook, and custom OIDC provider support. Deferred until local auth is stable.

---

## Transcoding Pipeline

### FFmpeg Integration

`FFmpegService` wraps FFmpeg/FFprobe with:
- Auto-detection of installation paths
- Hardware capability detection: NVENC (NVIDIA), QuickSync (Intel), VAAPI (Linux AMD/Intel)
- Metadata extraction replacing the current stub (resolution, duration, codec, frame rate)
- Embedded subtitle extraction (SRT, ASS, VTT)
- Chapter extraction from MKV/MP4

### On-the-Fly Transcoding

`GET /stream/{assetId}/transcode` produces HLS output (segmented `.m3u8` + `.ts` chunks). The client sends codec capabilities as query parameters; the Engine selects a transcode profile accordingly.

Quality profiles: Original, 1080p, 720p, 480p. Hardware-accelerated encoding when available; software fallback otherwise. Active sessions are tracked per user; temporary segments are cleaned up when the session ends.

### Shadow Transcoder

A background service that pre-creates mobile-optimised copies of video files so streaming is instant without on-the-fly transcoding.

- Runs on a configurable schedule (default: daily at 3:00 AM via `TranscodeJob` cron)
- Scans the library for video assets without a mobile-optimised variant
- Creates lower-bitrate variants stored at `{LibraryRoot}/.tuvima-shadow/{assetId}/{quality}.mp4`
- Respects hardware limits (default: 1 concurrent transcode)
- Progress reported via `TranscodeProgress` SignalR event
- Shadow copies are deleted automatically when the source asset is removed

Configuration in `config/transcoding.json`:

```json
{
  "quality_profiles": [
    { "name": "mobile", "resolution": "720p", "codec": "h264", "bitrate": "2M" },
    { "name": "tablet", "resolution": "1080p", "codec": "h264", "bitrate": "5M" }
  ],
  "schedule": { "enabled": true, "cron": "0 3 * * *" },
  "hardware_preference": "auto",
  "max_concurrent": 1,
  "shadow_storage_limit_gb": 500
}
```

---

## Music Domain Model

Music uses the same Collection/Work/Edition/MediaAsset hierarchy as all other media types, with these mappings:

| Library concept | Music concept |
|---|---|
| Collection | Album |
| Work | Track |
| Person (role: Artist) | Artist |

Track number maps to `Work.Ordinal`. Disc number is stored as the canonical value `disc_number`.

### MusicProcessor

Reads tag formats: ID3v2 (MP3), Vorbis comments (FLAC/OGG), MP4 atoms (M4A/AAC).

Extracted fields: title, artist, album, track number, disc number, genre, year, album art.

Magic byte detection:
- MP3: `FF FB` or `49 44 33`
- FLAC: `66 4C 61 43`
- OGG: `4F 67 67 53`
- M4A: ftyp box

### Providers

- **MusicBrainz** Ã¢â‚¬â€ zero-key, config-driven. Search by artist + album or MBID. Field weights: artist 0.9, album 0.85, year 0.9, genre 0.7.
- **Spotify** Ã¢â‚¬â€ requires a free API key. Contributes artist headshots, album art, genre, and popularity score. Metadata only Ã¢â‚¬â€ no streaming.

### Player

The audiobook player component (`AudioPlayer.razor`) is generalised to handle both audiobooks and music. Album view with track list and play queue.

---

## Interoperability

### OPDS 1.2 Catalog

Route: `/opds/`

Atom XML feeds for: root catalogue, search (OpenSearch), categories by media type / author / series, recently added. OPDS Page Streaming Extension for comic viewer apps.

Compatible with Moon Reader, KOReader, Calibre, Thorium Reader, and any other OPDS client.

Authentication via API key in URL parameter or HTTP Basic mapped to an Engine API key.

### Audiobookshelf-Compatible API

A subset of the Audiobookshelf API surface, allowing existing Audiobookshelf mobile apps to connect without modification:

- `GET /api/libraries`
- `GET /api/items/{id}`
- `GET /api/me/listening-sessions`

### Webhook System

Configuration in `config/webhooks.json`: list of endpoint URLs, event subscriptions, and HMAC secrets.

Events: `FileIngested`, `MetadataHydrated`, `TranscodeCompleted`, `PersonEnriched`.

Delivery: HTTP POST with `X-Tuvima-Signature` HMAC-SHA256 header.

Use cases include Discord/Telegram notifications for new content and automation triggers.

### Import Wizard

Guided import from existing media managers:

- **Plex** Ã¢â‚¬â€ reads `com.plexapp.plugins.library.db`, maps sections to Library Folders, imports watched status
- **Calibre** Ã¢â‚¬â€ reads `metadata.db`, imports books with existing metadata intact
- **Jellyfin** Ã¢â‚¬â€ reads NFO sidecar files alongside media, maps fields to Library claims

### PWA

- Web app manifest + service worker for installable experience
- Offline-cached shell (the app loads without the Engine; content requires the Engine)
- Push notifications for new content via Intercom bridge to the browser Push API

---

## Browse and Discovery Pages

### CollectionDetail

Route: `/collection/{id}`

Hero artwork with dominant color extraction. Collection metadata (name, year, franchise, Wikidata QID link). Works list grouped by media type. Person credits with headshots. Social Pivot links. "Hydrate from Wikidata" button.

### WorkDetail

Route: `/work/{id}`

Work metadata (title, author, year, series position). Edition list with format labels and file sizes. Claim history panel. Play/Read/Listen button.

### PersonDetail

Route: `/person/{id}`

Headshot, biography, occupation. Social links (Instagram, TikTok, Mastodon, website Ã¢â‚¬â€ using Actionable URI Schemes). Works grouped by role (author, narrator, director, cast member). Pseudonym relationships where applicable.

### New Home Sections

The Home page adds three sections above the existing Universe swimlanes:

1. **Continue Journey** Ã¢â‚¬â€ most recently accessed, incomplete items (queries UserState)
2. **Recently Added** Ã¢â‚¬â€ horizontal scroll of newest Collections (via `GET /collections/recent?limit=20`)
3. **Smart Collections** Ã¢â‚¬â€ "In Progress", "New This Week", "Unread" (auto-generated from metadata, pre-computed)

Faceted filtering is added to the Home page: filter by year range, media type, and author.

### Navigation Additions

- Breadcrumb trail: Home Ã¢â€ â€™ Collection Ã¢â€ â€™ Work
- Click-through from swimlane tiles to CollectionDetail
- Click-through from search results to WorkDetail
- "Next in series" link on WorkDetail (uses `Work.Ordinal`)

### Statistics Page

Route: `/statistics`

Library statistics and personal reading/watching history charts.

### API Additions Required

| Method | Route | Purpose |
|---|---|---|
| GET | `/collections/recent?limit=20` | Recently added Collections |
| GET | `/journey/continue?profileId={id}&limit=10` | Resume items |
| GET | `/collections/{id}/works` | Works for a Collection with full canonical values |
| GET | `/works/{id}/editions` | Editions for a Work with file metadata |
| GET | `/persons/{id}` | Person detail with social links and linked works |
| GET | `/collections` | Smart and user-created collections |
| GET | `/collections/{id}/items` | Items in a collection |

## Related

- [Collections and Playlists](collections.md)
- [Dashboard UI Architecture](dashboard-ui.md)
- [Settings Architecture and Review Queue](dashboard-ui.md)

## Current Dashboard/Product UI Model

Home, Read, Watch, Listen, and Search are the user-facing discovery and media surfaces. Detail pages and media rows/cards launch inline editing through the shared media editor. Review Queue is only for blocked or uncertain items that need human confirmation. Settings/Admin is for configuration and operational/system concerns. The old Review Queue concept is deprecated and must not be recreated; do not add new Review Queue routes, components, docs, or management-workbench flows.


