# Settings Architecture

Tuvima Library uses one rule for settings ownership: runtime configuration lives in JSON, while operational/user state lives in SQLite.

## JSON-Owned Configuration

Use JSON for settings that affect startup, background workers, provider behavior, library organization, or runtime policy:

- `config/core.json`: server identity, paths, organization templates, auth, storage policy.
- `config/libraries.json`: source libraries and watched roots.
- `config/providers/*.json` and `config/secrets/*.json`: provider capabilities, endpoints, credentials, weights, and limits.
- `config/ai.json`: model files, AI feature flags, schedules, vocabulary, hardware profile.
- `config/transcoding.json`: playback encoding, offline variants, cache retention.
- `config/hydration.json`, `config/pipelines.json`, `config/scoring.json`, `config/field_priorities.json`, `config/media_types.json`: metadata decision policy.
- `config/maintenance.json` and `config/writeback*.json`: cleanup and file write-back policy.

## SQLite-Owned State

Use SQLite for records created or changed by product workflows:

- profiles, profile external logins, and API keys.
- provider health and recent failures.
- review queue entries and resolution history.
- system activity, registry history, encode jobs, and playback/reading progress.

## Deprecated Public Settings

Palette, accent, and color customization are no longer product-facing settings. Any remaining palette files or accent fields are treated as internal design tokens used by theming services, not as user/admin options.

## UI Rule

Every visible settings page should declare its source of truth, editability, role requirement, validation shape, and whether a restart is required. Placeholder or local-only controls should stay hidden until backed by a real persistence path.
