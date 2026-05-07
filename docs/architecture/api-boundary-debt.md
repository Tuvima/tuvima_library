# API Boundary Debt

Wave 7 removed direct database access from `SystemEndpoints.cs` by moving orphan-image reference queries into `OrphanImageReferenceReadService`.

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
