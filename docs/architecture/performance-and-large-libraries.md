---
title: "Performance and Large Libraries"
summary: "Rules for keeping Tuvima Library responsive with large local media collections."
audience: "developer"
category: "architecture"
product_area: "performance"
tags:
  - "performance"
  - "paging"
  - "sqlite"
---

# Performance and Large Libraries

Large library surfaces must be bounded by default. API endpoints that can return works, assets, people, ingestion rows, watch-folder files, activity, reviews, or search results should use shared paging contracts and clamp caller-provided limits on the server.

Use server-side filtering and sorting whenever practical. Avoid loading all rows into memory and then applying search, type filters, or status filters in C# for request-time screens.

Blazor Server pages must not render unbounded rows or cards. Use MudBlazor server data, Blazor `Virtualize`, or explicit "load more" paging for large grids and track lists. Add stable keys for repeated rows where row identity matters.

Avoid N+1 repository loops. When a page needs related people, collections, canonical values, or artwork for many parent rows, add a batch read method and map the result in memory.

SQLite indexes should be added for new high-volume query patterns. Prioritize joins and filters over `media_assets`, `editions`, `works`, `canonical_values`, `canonical_value_arrays`, `person_media_links`, `ingestion_log`, and `identity_jobs`.

Large-list read paths should emit debug timing logs with operation name, elapsed milliseconds, offset or cursor, limit, returned item count, and `has_more`. Slow reads over one second should log a warning. Do not log sensitive paths, full queries, secrets, or user media metadata.

Performance tests should use generated temp SQLite data or in-memory fixtures. They must not depend on the user's real database, watch folder, media files, local AI models, or network.

