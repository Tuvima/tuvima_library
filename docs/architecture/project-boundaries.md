---
title: "Project Boundaries"
summary: "Rules for keeping Domain, Engine, Storage, Providers, Ingestion, and Dashboard dependency boundaries clean."
audience: "developer"
category: "architecture"
product_area: "system"
---

# Project Boundaries

Tuvima Library keeps project dependencies pointed inward so UI and endpoint work does not accidentally couple to SQLite or other infrastructure details.

- `MediaEngine.Domain` is the core model and pure domain rules. Keep it dependency-light.
- `MediaEngine.Contracts` contains DTOs that are safe for API, Dashboard, and other clients to share.
- `MediaEngine.Application` contains use-case/read-model interfaces and request/response shapes that are not UI-specific and not storage implementations.
- `MediaEngine.Storage` owns SQLite, Dapper, schema behavior, and repository implementations.
- `MediaEngine.Api` is the composition root. It wires Application/Domain abstractions to Storage and API read-service implementations.
- `MediaEngine.Web` talks to the Engine through HTTP and SignalR. It must not reference `MediaEngine.Storage`.

Domain aggregates expose child collections and property bags as read-only views.
Normal mutation must go through named aggregate methods, such as `AddWork`,
`AddEdition`, or `SetExternalIdentifier`. Repository materialization should use
those same methods, or an explicitly named hydration helper if bulk loading ever
requires one. Do not make Domain depend on serialization, Dapper, SQLite, HTTP,
or UI concerns to support persistence.

Endpoint methods should stay thin: validate route/query/body inputs, call a service or query object, and return HTTP results. SQL, row mapping, and fallback query rules belong in Storage or clearly named API read services.

