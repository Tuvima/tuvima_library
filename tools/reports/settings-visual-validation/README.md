# Settings visual validation

Validated on 2026-08-04 against the supplied target-state settings screenshots using the live Engine and Dashboard at 1920 x 1080, 1280 x 720, and 1024 x 768.

## Results

| Route | Reference area | Result |
|---|---|---|
| `/settings/profile` | Profile dashboard | Pass: 5/7 profile and activity split, full-width Continue, 7/5 history and genres split |
| `/settings/playback/general` | Playback defaults | Pass: unique resume/sync controls plus non-duplicated watching, listening, subtitle, and reader summaries |
| `/settings/playback/watching` | Watching controls | Pass: value-aligned speed scale, skip controls, episode behavior, quality, and artwork |
| `/settings/playback/listening` | Listening controls | Pass: value-aligned audiobook/crossfade scales; internal history and chapter thresholds are collapsed under Advanced |
| `/settings/playback/reading` | Reading controls | Pass: mode, typography, layout, wake/progress/page toggles, and preview |
| `/settings/playback/subtitles` | Subtitle controls | Pass: language, forced mode, audio language, size, background, position, style, and live preview |
| `/settings/system` | System Overview | Pass: consolidated live operational dashboard and one Engine status |
| `/settings/providers` | Metadata Providers | Pass: three local provider stages and live provider assignment data |
| `/settings/delivery` | Playback & Delivery | Pass: four local sections; no Direct Play or subtitle placeholder tabs |
| `/settings/access` | Users & Access | Pass: four local sections and one real API-key location |
| `/settings/ai` | Local AI | Pass: four consolidated local sections using live hardware and enrichment state |
| `/settings/plugins` | Plugins | Pass: installed list/detail manager, four local tabs, separate approved catalog and danger area, no fake install action |
| `/settings/developer` | Developer Tools | Pass: consolidated harness, tester entries, result area, and protected danger area |

Every page was checked for a level-one page heading, horizontal overflow, and the application render-failure state. No render failures or document-level horizontal overflow remained in the final pass.

Slider marker positions are calculated from the real numeric ranges rather than distributed evenly. Compact switches disable ripple highlights, center the thumb vertically, and keep the checked thumb inside the component boundary so table cells cannot clip it.

## Captures

- `profile-desktop.png`
- `system-desktop.png`
- `providers-desktop.png`
- `delivery-desktop.png`
- `access-desktop.png`
- `ai-desktop.png`
- `plugins-desktop.png`
- `developer-desktop.png`

Additional captures cover Libraries, Ingestion, Review Queue, and Activity & Audit.

Responsive behavior is implemented through the shared shell breakpoint: the fixed rail is replaced by the Settings page selector at 840px, dashboard grids collapse to one column, and local tab strips become horizontally scrollable when necessary. Profile no longer has a 1440px content cap and uses the full available settings canvas.
