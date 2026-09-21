# Canonical artwork architecture

Tuvima stores artwork as one managed image with many contextual uses.

## Durable model

- `image_cache` remains the low-level download/content cache. Its SHA-256 identity is reused by the artwork layer.
- `artwork_assets` is the canonical image identity. It owns the original managed path, S/M/L renditions, measured dimensions, aspect class, palette, perceptual hash, and source provenance. `content_hash` is unique.
- `entity_artwork_links` assigns an existing image to an entity and semantic role (`Primary`, `Background`, `Portrait`, or `Logo`). Preferred and user-override state belongs to this link, not to the image.
- `artwork_asset_context` provides searchable, many-to-one context: entity label/type, media type, year, role, provider, and canonical identifiers.

Image shape is deliberately independent of semantic role. `aspect_class` remains `Portrait`, `Square`, `LandscapeWide`, `BannerStrip`, or `UnsupportedRect`.

Automatic structural artwork is a derived presentation. It remains a bounded ordered list of child artwork IDs and is never written to `artwork_assets`.

## Compatibility and migration

Startup migration `007_canonical_artwork_assets` creates the normalized tables and projects every legacy `entity_assets` row into them. Rows backed by `image_cache` converge on the cache SHA-256; legacy rows without a known content hash receive a stable migration identity. Work and person search contexts are generated from canonical metadata.

`EntityAssetRepository` is the temporary compatibility bridge for existing ingestion/provider workers: every artwork upsert now resolves a SHA-256 canonical asset and writes an entity link. Legacy rows continue to be projected so current Home, Read, Watch, Listen, Collections, details, and editor consumers remain shape-aware while those readers migrate to the link resolver. Linking an existing asset writes only a compatibility reference to the same managed paths; it never copies image bytes or regenerates renditions.

Collection path columns are compatibility projections of the preferred collection link. Removing a collection link clears the projection but keeps the canonical image and other entity links.

Physical cleanup treats every canonical original/rendition as referenced. Removing one link never deletes shared files; orphan retention remains the responsibility of the normal managed-asset cleanup policy.

## API and UI

- Entity gallery: `GET /api/v1/display/artwork`
- Bounded asset search/picker: `GET /api/v1/display/artwork/assets`
- Lazy entity variants: `GET /api/v1/display/artwork/entities/{type}/{id}`
- Link existing, upload, URL import, remove link, and rendition streaming use the sibling artwork endpoints.
- `ArtworkWorkspace` owns browse/edit presentation, role navigation, variants, metadata, zoom, upload, URL import, canonical library picking, and unlink/automatic restore.

VIEW keeps Personal media destinations separate from Library Artwork > Media, People, and Universes. Grid requests are paged; full variants are loaded only when an entity opens, and grids use small renditions.
