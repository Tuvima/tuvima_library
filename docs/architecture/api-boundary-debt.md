---
title: "API Boundary Debt"
summary: "Known API boundary cleanup items and migration guidance for moving SQL-heavy endpoint behavior into services."
audience: "developer"
category: "architecture"
product_area: "api"
status: "internal"
---

# API Boundary Debt

Wave 7 removed direct database access from `SystemEndpoints.cs` by moving orphan-image reference queries into `OrphanImageReferenceReadService`.

Sprint 2 removed profile overview SQL from `ProfileEndpoints.cs`, moved
person and character credit projections into `PersonCreditReadService`, and
carved the small operational aggregate portion of `GET /library/overview` into
`LibraryOverviewReadService`.

Sprint 4 moved the first collection read routes behind focused API read
services: `CollectionBrowseReadService` for `GET /collections/`,
`CollectionSearchReadService` for `GET /collections/search`, and
`CollectionMediaLookupReadService` for `GET /collections/media-lookup` plus
curated item metadata projection. It also moved media editor navigator,
membership suggestion, preview, and apply logic into
`MediaEditorNavigationReadService`, and moved `GET /metadata/claims/{entityId}`
into `MetadataClaimHistoryReadService`.

## Compliant Endpoint Pattern

Sprint 1 reinforced the guardrails around endpoint boundaries but did not pay
down the full legacy SQL allowlist below. New and touched endpoint code should
look like these existing patterns:

- `ProgressEndpoints.cs` validates route/query input and delegates journey
  projection work to `IJourneyReadService`.
- `IngestionEndpoints.cs` keeps batch item projection behind
  `IIngestionBatchReadService` instead of building SQL in the endpoint.
- `SystemEndpoints.cs` delegates orphan-image reference checks to
  `IOrphanImageReferenceReadService`.
- `PersonEndpoints.cs` uses focused person read services for aliases,
  presence, works, scoped person summaries, and person credit projections.
- `ProfileEndpoints.cs` delegates account overview projection work to
  `IProfileOverviewReadService`.
- `LibraryEndpoints.cs` delegates overview operational aggregates to
  `ILibraryOverviewReadService`; broader library browse and management SQL
  remains legacy debt.

The endpoint should stay an HTTP adapter: validate input, call a repository or
read service, preserve cancellation flow, and return the established DTO shape.
SQL belongs in Storage repositories or focused API read services that use
`IDatabaseConnection.CreateConnection()` with a short-lived disposed connection.

## Remaining Allowlist

| File | Risk | Reason still allowlisted | Suggested target |
| --- | --- | --- | --- |
| `CharacterEndpoints.cs` | Medium | Character graph projections combine several relationship tables. | `CharacterReadService` |
| `CollectionEndpoints.cs` | High | Browse/search/media lookup read routes were extracted in Sprint 4; large group/detail, system-view, management catalog, preview, artwork, and mutation workflows still mix reads and commands. | Continue splitting focused collection detail, management, and command services |
| `ItemCanonicalEndpoints.cs` | Medium | Canonical value mutation paths need careful contract preservation. | `ItemCanonicalService` |
| `LibraryEndpoints.cs` | High | Broad browse and management surface with paging/sort behavior. | `LibraryBrowseReadService` |
| `LibraryItemEndpoints.cs` | High | Item projection, review, mutation, and delete behavior are coupled. | `LibraryItemCommandService` plus read service |
| `MetadataEndpoints.cs` | High | Claim history was extracted in Sprint 4; metadata edit, artwork, provider matching, editor-context, and cache behavior remains broad. | `MetadataEditorService` and focused artwork/editor-context read services |
| `MetadataEndpoints.MediaEditorNavigator.cs` | Low | Route mapping is now thin, but the file remains listed until follow-up confirms no direct database access returns during membership apply hardening. | Remove after guardrail coverage confirms the new service boundary |
| `UniverseGraphEndpoints.cs` | High | Graph traversal and filtering needs focused regression tests. | `UniverseGraphReadService` |
| `WorkEndpoints.cs` | Medium | Work detail projections include direct SQL. | `WorkReadService` |

## Recommended Order

1. Continue small, test-backed read-only carve-outs from `LibraryEndpoints.cs`.
2. Move `CharacterEndpoints.cs` graph projections behind a focused read service.
3. Move `MetadataEndpoints.MediaEditorNavigator.cs` projections behind a focused read service.
4. Mutation-heavy metadata, item, collection, and universe graph files after stronger route-level tests exist.
