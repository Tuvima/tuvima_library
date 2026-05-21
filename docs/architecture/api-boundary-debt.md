# API Boundary Debt

Wave 7 removed direct database access from `SystemEndpoints.cs` by moving orphan-image reference queries into `OrphanImageReferenceReadService`.

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
  presence, works, and scoped person summaries.

The endpoint should stay an HTTP adapter: validate input, call a repository or
read service, preserve cancellation flow, and return the established DTO shape.
SQL belongs in Storage repositories or focused API read services that use
`IDatabaseConnection.CreateConnection()` with a short-lived disposed connection.

## Remaining Allowlist

| File | Risk | Reason still allowlisted | Suggested target |
| --- | --- | --- | --- |
| `CharacterEndpoints.cs` | Medium | Character graph projections combine several relationship tables. | `CharacterReadService` |
| `CollectionEndpoints.cs` | High | Mixed collection reads, writes, ordering, and management behavior. | Split `CollectionReadService` and storage repositories |
| `ItemCanonicalEndpoints.cs` | Medium | Canonical value mutation paths need careful contract preservation. | `ItemCanonicalService` |
| `LibraryEndpoints.cs` | High | Broad browse and management surface with paging/sort behavior. | `LibraryBrowseReadService` |
| `LibraryItemEndpoints.cs` | High | Item projection, review, mutation, and delete behavior are coupled. | `LibraryItemCommandService` plus read service |
| `MetadataEndpoints.cs` | High | Metadata edit and provider matching behavior is broad. | `MetadataEditorService` |
| `MetadataEndpoints.MediaEditorNavigator.cs` | Medium | API-specific navigation projections. | `MediaEditorNavigationReadService` |
| `PersonCreditQueries.cs` | Medium | Complex person/character credit projections. | `PersonCreditReadService` |
| `PersonEndpoints.cs` | Medium | Some direct query helpers remain around person details. | Extend existing person read services |
| `ProfileEndpoints.cs` | Medium | Profile overview projections still query library/user state directly. | `ProfileOverviewReadService` |
| `UniverseGraphEndpoints.cs` | High | Graph traversal and filtering needs focused regression tests. | `UniverseGraphReadService` |
| `WorkEndpoints.cs` | Medium | Work detail projections include direct SQL. | `WorkReadService` |

## Recommended Order

1. `ProfileEndpoints.cs` overview helpers.
2. `PersonEndpoints.cs` remaining direct reads.
3. `PersonCreditQueries.cs` as a dedicated read service.
4. Small read-only portions of `LibraryEndpoints.cs`.
5. Mutation-heavy metadata, item, collection, and universe graph files after stronger route-level tests exist.
