# Endpoint registration ledger

Generated from source by `generate-endpoint-ledger.ps1`. It is a syntactic inventory, not proof that a guard authorizes the correct resource. Re-run after route changes and reconcile against `endpoint-matrix.md`. Routes containing interpolated helper fragments are preserved as written.

| Method | Registered route | Declared guard | Environment | Source |
| --- | --- | --- | --- | --- |
| GET | `/dev/check-keys` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/DevSeedEndpoints.cs:1019` |
| POST | `/dev/seed-library` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/DevSeedEndpoints.cs:1022` |
| POST | `/dev/wipe` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/DevSeedEndpoints.cs:1025` |
| POST | `/dev/full-test` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/DevSeedEndpoints.cs:1028` |
| POST | `/dev/reingest-library` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/DevSeedEndpoints.cs:1031` |
| GET | `/dev/pipeline-status` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/DevSeedEndpoints.cs:1034` |
| POST | `/dev/view-photo-harness` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/DevSeedEndpoints.cs:1037` |
| POST | `/dev/generate-ingestion-edge-fixtures` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/DevSeedEndpoints.cs:1045` |
| POST | `/dev/integration-test` | fallback policy / middleware | Development only | `src/MediaEngine.Api/DevSupport/IntegrationTestEndpoints.cs:720` |
| GET | `/accounts/me` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:23` |
| GET | `/accounts/me/external-logins` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:30` |
| POST | `/accounts/me/external-logins` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:34` |
| DELETE | `/accounts/me/external-logins/{loginId:guid}` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:44` |
| GET | `/accounts/` | RequireAuthorization(AuthPolicies.Authenticated), RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:71` |
| POST | `/accounts/` | RequireAuthorization(AuthPolicies.Authenticated), RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:79` |
| PUT | `/accounts/{accountId:guid}/profiles/{profileId:guid}` | RequireAuthorization(AuthPolicies.Authenticated), RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:113` |
| DELETE | `/accounts/{accountId:guid}/profiles/{profileId:guid}` | RequireAuthorization(AuthPolicies.Authenticated), RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:128` |
| POST | `/accounts/invitations` | RequireAuthorization(AuthPolicies.Authenticated), RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Endpoints/AccountEndpoints.cs:152` |
| GET | `/activity/summary` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:23` |
| GET | `/activity/batches` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:31` |
| GET | `/activity/batches/{batchId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:68` |
| GET | `/activity/batches/{batchId:guid}/presentation` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:84` |
| GET | `/activity/batches/{batchId:guid}/media-groups` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:100` |
| GET | `/activity/batches/{batchId:guid}/groups` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:118` |
| GET | `/activity/batches/{batchId:guid}/insights` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:131` |
| GET | `/activity/batches/{batchId:guid}/items` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:143` |
| GET | `/activity/batches/{batchId:guid}/events` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:164` |
| GET | `/activity/batches/{batchId:guid}/items/{assetId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:182` |
| GET | `/activity/people` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:199` |
| GET | `/activity/recent` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:236` |
| POST | `/activity/prune` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:255` |
| GET | `/activity/stats` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:275` |
| PUT | `/activity/retention` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:294` |
| GET | `/activity/by-types` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:319` |
| GET | `/activity/run/{runId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ActivityEndpoints.cs:341` |
| GET | `/admin/api-keys` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs:40` |
| POST | `/admin/api-keys` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs:59` |
| DELETE | `/admin/api-keys/{id:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs:94` |
| DELETE | `/admin/api-keys` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs:118` |
| GET | `/admin/provider-configs/{providerId}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs:138` |
| METHODS | `/admin/provider-configs/{providerId}/{configKey}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs:158` |
| DELETE | `/admin/provider-configs/{providerId}/{configKey}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AdminEndpoints.cs:184` |
| GET | `/ai/status` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:26` |
| GET | `/ai/models` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:46` |
| POST | `/ai/models/{role}/download` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:66` |
| DELETE | `/ai/models/{role}/download` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:93` |
| POST | `/ai/models/{role}/load` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:120` |
| POST | `/ai/models/{role}/unload` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:147` |
| GET | `/ai/config` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:177` |
| PUT | `/ai/config` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:188` |
| GET | `/ai/profile` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:207` |
| POST | `/ai/benchmark` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:217` |
| DELETE | `/ai/benchmark` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:229` |
| GET | `/ai/resources` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:243` |
| GET | `/ai/enrichment/progress` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/AiEndpoints.cs:261` |
| GET | `/auth/bootstrap/status` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:20` |
| POST | `/auth/login` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:25` |
| POST | `/auth/external-session` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:50` |
| POST | `/auth/invitations/accept` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:71` |
| POST | `/auth/session/validate` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:77` |
| GET | `/auth/sessions` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:83` |
| DELETE | `/auth/sessions/{sessionId:guid}` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:103` |
| POST | `/auth/password/change` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:115` |
| POST | `/auth/password/recovery-codes` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:127` |
| POST | `/auth/password/recover` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:138` |
| POST | `/auth/password/reset/begin` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:149` |
| POST | `/auth/password/reset/complete` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:155` |
| POST | `/auth/passkeys/login/options` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:162` |
| POST | `/auth/passkeys/login/complete` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:173` |
| POST | `/auth/passkeys/registration/options` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:181` |
| POST | `/auth/passkeys/registration/complete` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:189` |
| GET | `/auth/passkeys` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:199` |
| DELETE | `/auth/passkeys/{credentialId}` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:205` |
| POST | `/auth/passkeys/elevation/options` | RequireAuthorization(AuthPolicies.AdministratorRole) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:214` |
| POST | `/auth/passkeys/elevation/complete` | RequireAuthorization(AuthPolicies.AdministratorRole) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:220` |
| PUT | `/auth/profiles/{profileId:guid}/pin` | RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:227` |
| PUT | `/auth/profiles/{profileId:guid}/administrator-pin` | RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:234` |
| POST | `/auth/elevation` | RequireAuthorization(AuthPolicies.AdministratorRole) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:242` |
| GET | `/auth/elevation` | RequireAuthorization(AuthPolicies.AdministratorRole) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:248` |
| DELETE | `/auth/elevation` | RequireAuthorization(AuthPolicies.AdministratorRole) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:254` |
| POST | `/auth/session/switch-profile` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:260` |
| POST | `/auth/intercom-token` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/AuthenticationEndpoints.cs:281` |
| GET | `//metadata/{entityId:guid}/canon-discrepancies` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CanonEndpoints.cs:20` |
| GET | `/assets/{id:guid}/capabilities` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/CapabilityEndpoints.cs:12` |
| GET | `/capabilities/summary` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/CapabilityEndpoints.cs:26` |
| GET | `/library/portraits/{portraitId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CharacterEndpoints.cs:35` |
| GET | `/library/characters/{fictionalEntityId:guid}/portraits` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CharacterEndpoints.cs:96` |
| PUT | `/library/characters/{fictionalEntityId:guid}/portraits/{portraitId:guid}/default` | RequireAnyRole, RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/CharacterEndpoints.cs:126` |
| GET | `/library/persons/{personId:guid}/character-roles` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CharacterEndpoints.cs:148` |
| GET | `/library/universes/{universeQid}/characters` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CharacterEndpoints.cs:167` |
| GET | `/library/assets/{entityId}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CharacterEndpoints.cs:216` |
| POST | `/library/enrichment/universe/trigger` | RequireAnyRole, RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/CharacterEndpoints.cs:235` |
| POST | `/api/v1/oauth/device_authorization` | AllowAnonymous | Production | `src/MediaEngine.Api/Endpoints/ClientAuthorizationEndpoints.cs:18` |
| POST | `/api/v1/oauth/token` | AllowAnonymous, RequireAuthorization(AuthPolicies.DashboardInteractive) | Production | `src/MediaEngine.Api/Endpoints/ClientAuthorizationEndpoints.cs:45` |
| GET | `/api/v1/pairing/review/{userCode}` | RequireAuthorization(AuthPolicies.DashboardInteractive) | Production | `src/MediaEngine.Api/Endpoints/ClientAuthorizationEndpoints.cs:78` |
| POST | `/api/v1/pairing/decision` | RequireAuthorization(AuthPolicies.DashboardInteractive), RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/ClientAuthorizationEndpoints.cs:90` |
| GET | `/api/v1/devices/` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/ClientAuthorizationEndpoints.cs:116` |
| GET | `/api/v1/devices/current` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/ClientAuthorizationEndpoints.cs:125` |
| PUT | `/api/v1/devices/current/capabilities` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.PlaybackWrite) | Production | `src/MediaEngine.Api/Endpoints/ClientAuthorizationEndpoints.cs:134` |
| DELETE | `/api/v1/devices/{deviceId:guid}` | RequireAuthorization(AuthPolicies.Authenticated) | Production | `src/MediaEngine.Api/Endpoints/ClientAuthorizationEndpoints.cs:149` |
| GET | `/collections/{collectionId:guid}/series-manifest` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:52` |
| GET | `/collections/` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:68` |
| GET | `/collections/search` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:80` |
| GET | `/collections/parents` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:97` |
| GET | `/collections/{id:guid}/children` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:147` |
| GET | `/collections/{id:guid}/parent` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:165` |
| GET | `/collections/{id:guid}/related` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:196` |
| GET | `/collections/{collectionId:guid}/group-detail` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:296` |
| GET | `/collections/artist-group-detail` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:583` |
| GET | `/collections/artist-detail-by-name` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:766` |
| GET | `/collections/system-view-detail` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:915` |
| GET | `/collections/content-groups` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1220` |
| GET | `/collections/system-views` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1396` |
| GET | `/collections/managed` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1416` |
| GET | `/collections/catalog` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1431` |
| POST | `/collections/reconcile` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1447` |
| GET | `/collections/managed/counts` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1471` |
| GET | `/collections/media-lookup` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1490` |
| GET | `/collections/{id:guid}/summary` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1533` |
| GET | `/collections/{id:guid}/items` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1552` |
| POST | `/collections/{id:guid}/items` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1581` |
| DELETE | `/collections/{id:guid}/items/{itemId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1640` |
| PUT | `/collections/{id:guid}/items/reorder` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1680` |
| GET | `/collections/{id:guid}/artwork/{slot}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1723` |
| POST | `/collections/{id:guid}/artwork/{slot}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1763` |
| DELETE | `/collections/{id:guid}/artwork/{slot}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1848` |
| PUT | `/collections/{id:guid}/enabled` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1890` |
| PUT | `/collections/{id:guid}/featured` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1919` |
| GET | `/collections/resolve/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:1950` |
| GET | `/collections/resolve/by-name` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2025` |
| GET | `/collections/by-location/{location}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2068` |
| POST | `/collections/preview` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2108` |
| POST | `/collections/` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2148` |
| PUT | `/collections/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2293` |
| DELETE | `/collections/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2412` |
| GET | `/collections/field-values/{field}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2450` |
| GET | `/collections/entity-field-values/{field}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2466` |
| GET | `/collections/{id:guid}/placements` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2487` |
| PUT | `/collections/{id:guid}/placements` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionEndpoints.cs:2507` |
| GET | `/personal-media/galleries` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/CollectionPersonalMediaEndpoints.cs:13` |
| GET | `/{id:guid}/personal-media` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/CollectionPersonalMediaEndpoints.cs:29` |
| POST | `/{id:guid}/personal-media` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/CollectionPersonalMediaEndpoints.cs:54` |
| PUT | `/{id:guid}/personal-media/{sourceId:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/CollectionPersonalMediaEndpoints.cs:75` |
| DELETE | `/{id:guid}/personal-media/{sourceId:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/CollectionPersonalMediaEndpoints.cs:97` |
| POST | `/debug/lookup` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/DebugEndpoints.cs:32` |
| POST | `/debug/search` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/DebugEndpoints.cs:196` |
| POST | `/debug/enrich` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/DebugEndpoints.cs:258` |
| POST | `/debug/enrich-universe` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/DebugEndpoints.cs:410` |
| POST | `/metadata/pass2/trigger` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/DeferredEnrichmentEndpoints.cs:21` |
| GET | `/metadata/pass2/status` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/DeferredEnrichmentEndpoints.cs:36` |
| GET | `/api/v1/details/{entityType}/{id:guid}` | RequireClientScope(ClientApiScopes.LibraryRead) | Production | `src/MediaEngine.Api/Endpoints/DetailEndpoints.cs:20` |
| PUT | `/api/v1/details/{entityType}/{id:guid}/sequence-default` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/DetailEndpoints.cs:50` |
| GET | `/api/v1/display/home` | RequireClientScope(ClientApiScopes.LibraryRead) | Production | `src/MediaEngine.Api/Endpoints/DisplayEndpoints.cs:21` |
| GET | `/api/v1/display/browse` | RequireClientScope(ClientApiScopes.LibraryRead) | Production | `src/MediaEngine.Api/Endpoints/DisplayEndpoints.cs:28` |
| GET | `/api/v1/display/continue` | RequireClientScope(ClientApiScopes.ProgressRead) | Production | `src/MediaEngine.Api/Endpoints/DisplayEndpoints.cs:67` |
| GET | `/api/v1/display/contributor-shelves` | RequireClientScope(ClientApiScopes.LibraryRead) | Production | `src/MediaEngine.Api/Endpoints/DisplayEndpoints.cs:83` |
| GET | `/api/v1/display/search` | RequireClientScope(ClientApiScopes.LibraryRead) | Production | `src/MediaEngine.Api/Endpoints/DisplayEndpoints.cs:90` |
| GET | `/api/v1/display/shelves/{shelfKey}` | RequireClientScope(ClientApiScopes.LibraryRead) | Production | `src/MediaEngine.Api/Endpoints/DisplayEndpoints.cs:104` |
| GET | `/api/v1/display/groups/{groupId:guid}` | RequireClientScope(ClientApiScopes.LibraryRead) | Production | `src/MediaEngine.Api/Endpoints/DisplayEndpoints.cs:137` |
| GET | `/ingestion/refresh-schedule/` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/EnrichmentRefreshEndpoints.cs:17` |
| GET | `/ingestion/refresh-schedule/{entityType}/{entityId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/EnrichmentRefreshEndpoints.cs:31` |
| POST | `/ingestion/refresh-schedule/{entityType}/{entityId:guid}/run-now` | RequireAnyRole, RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/EnrichmentRefreshEndpoints.cs:47` |
| GET/HEAD | `/stream/hls/{grant}/{packageId:guid}/{**resourcePath}` | AllowAnonymous | Production | `src/MediaEngine.Api/Endpoints/HlsStreamEndpoints.cs:9` |
| GET | `/ingestion/operations` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:26` |
| GET | `/ingestion/presentation` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:38` |
| GET | `/ingestion/media-groups` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:53` |
| GET | `/ingestion/recent-additions` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:67` |
| GET | `/ingestion/batches/{batchId:guid}/media-groups/{groupId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:85` |
| GET | `/ingestion/batches/{batchId:guid}/media-groups/{groupId:guid}/children` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:100` |
| POST | `/ingestion/assets/{assetId:guid}/reread-metadata` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:116` |
| POST | `/ingestion/scan` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:140` |
| POST | `/ingestion/library-scan` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:184` |
| GET | `/ingestion/watch-folder` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:229` |
| POST | `/ingestion/rescan` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:283` |
| POST | `/ingestion/reconcile` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:339` |
| GET | `/ingestion/batches` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:361` |
| GET | `/ingestion/batches/attention-count` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:375` |
| GET | `/ingestion/batches/{id:guid}/items` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:387` |
| GET | `/ingestion/batches/{id:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:430` |
| POST | `/ingestion/upload` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/IngestionEndpoints.cs:446` |
| GET | `/library/items/editor-suggestions/{field}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:42` |
| PUT | `/library/items/{entityId:guid}/preferences` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:60` |
| PUT | `/library/items/{entityId:guid}/display-overrides` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:168` |
| POST | `/library/items/{entityId:guid}/canonical-search` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:241` |
| POST | `/library/items/{entityId:guid}/canonical-apply` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:350` |
| GET | `/library/items/{entityId:guid}/editor-preferences/{profileId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:567` |
| PUT | `/library/items/{entityId:guid}/editor-preferences/{profileId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:581` |
| POST | `/library/items/{entityId:guid}/retail-match` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:644` |
| POST | `/library/items/{entityId:guid}/wikidata-match` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ItemCanonicalEndpoints.cs:1049` |
| GET | `/library/overview` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:28` |
| GET | `/library/works` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:85` |
| POST | `/library/batch-edit/preview` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:130` |
| POST | `/library/batch-edit` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:188` |
| GET | `/library/universe-candidates` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:289` |
| POST | `/library/universe-candidates/{workId:guid}/accept` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:311` |
| POST | `/library/universe-candidates/{workId:guid}/reject` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:348` |
| POST | `/library/universe-candidates/batch-accept` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:381` |
| GET | `/library/universe-unlinked` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:452` |
| POST | `/library/universe-assign` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryEndpoints.cs:464` |
| GET | `/library/items` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:32` |
| GET | `/library/items/{entityId}/detail` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:66` |
| GET | `/library/items/counts` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:82` |
| GET | `/library/items/state-counts` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:89` |
| GET | `/library/items/type-counts` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:99` |
| POST | `/library/items/{entityId}/apply-match` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:106` |
| POST | `/library/items/{entityId}/create-manual` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:223` |
| DELETE | `/library/items/{entityId}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:258` |
| POST | `/library/items/{entityId}/reject` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:286` |
| POST | `/library/items/batch/approve` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:328` |
| POST | `/library/items/batch/delete` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:350` |
| POST | `/library/items/batch/reject` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:398` |
| POST | `/library/items/{entityId:guid}/recover` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:451` |
| POST | `/library/items/{entityId:guid}/provisional` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:492` |
| GET | `/library/items/{entityId:guid}/history` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/LibraryItemEndpoints.cs:556` |
| GET | `/libraries/view-summary` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/LibraryMutationEndpoints.cs:19` |
| POST | `/libraries/mutations` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/LibraryMutationEndpoints.cs:25` |
| POST | `/settings/libraries/{libraryId:guid}/reorganization/plan` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/LibraryReorganizationEndpoints.cs:16` |
| POST | `/settings/libraries/{libraryId:guid}/reorganization/execute` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/LibraryReorganizationEndpoints.cs:40` |
| GET | `/maintenance/retag-sweep/state` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MaintenanceEndpoints.cs:24` |
| POST | `/maintenance/retag-sweep/apply` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/MaintenanceEndpoints.cs:44` |
| POST | `/maintenance/retag-sweep/run-now` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/MaintenanceEndpoints.cs:59` |
| POST | `/maintenance/retag-sweep/retry/{assetId:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/MaintenanceEndpoints.cs:74` |
| POST | `/maintenance/initial-sweep/run` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/MaintenanceEndpoints.cs:98` |
| POST | `/maintenance/storage/run` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/MaintenanceEndpoints.cs:112` |
| GET | `/metadata/claims/{entityId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:68` |
| GET | `/metadata/conflicts` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:82` |
| METHODS | `/metadata/lock-claim` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:102` |
| POST | `/metadata/hydrate/{entityId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:176` |
| POST | `/metadata/search` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:234` |
| PUT | `/metadata/{entityId:guid}/override` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:312` |
| POST | `/metadata/{entityId:guid}/reclassify` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:420` |
| GET | `/metadata/{entityId:guid}/editor-context` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:550` |
| GET | `/metadata/{entityId:guid}/artwork/{scopeId}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:610` |
| POST | `/metadata/{entityId:guid}/artwork/{scopeId}/refresh-provider` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:638` |
| GET | `/metadata/{entityId:guid}/artwork` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:713` |
| POST | `/metadata/{entityId:guid}/cover` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:794` |
| POST | `/metadata/{entityId:guid}/artwork/{scopeId}/{assetType}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:887` |
| POST | `/metadata/{entityId:guid}/artwork/{scopeId}/{assetType}/from-url` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:985` |
| POST | `/metadata/{entityId:guid}/artwork/{assetType}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1093` |
| PUT | `/metadata/artwork/{variantId:guid}/preferred` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1178` |
| DELETE | `/metadata/artwork/{variantId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1216` |
| GET | `/metadata/wikidata-test` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1291` |
| POST | `/metadata/search-all` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1373` |
| GET | `/metadata/{entityId:guid}/search-cache` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1489` |
| PUT | `/metadata/{entityId:guid}/search-cache` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1505` |
| GET | `/metadata/canonical/{entityId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1526` |
| POST | `/metadata/{entityId:guid}/cover-from-url` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1610` |
| POST | `/metadata/labels/resolve` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1706` |
| GET | `/metadata/{qid}/aliases` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.cs:1737` |
| GET | `/{entityId:guid}/navigator` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.MediaEditorNavigator.cs:15` |
| GET | `/{entityId:guid}/membership-suggestions` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.MediaEditorNavigator.cs:31` |
| POST | `/{entityId:guid}/membership-preview` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.MediaEditorNavigator.cs:49` |
| POST | `/{entityId:guid}/membership-apply` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/MetadataEndpoints.MediaEditorNavigator.cs:66` |
| GET | `/settings/network` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:19` |
| PUT | `/settings/network` | RequireAdmin, RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:25` |
| GET | `/network/status` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:69` |
| GET | `/network/readiness` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:74` |
| POST | `/network/tests/local` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:83` |
| POST | `/network/tests/remote` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:88` |
| POST | `/network/bandwidth-test` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:93` |
| POST | `/network/port-change/check` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:99` |
| POST | `/network/port-change/apply` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:106` |
| POST | `/network/router/renew` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:143` |
| POST | `/network/reset` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/NetworkEndpoints.cs:155` |
| GET | `/operations/` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/OperationsEndpoints.cs:17` |
| GET | `/operations/{id:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/OperationsEndpoints.cs:32` |
| GET | `/operations/summary` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/OperationsEndpoints.cs:54` |
| POST | `/operations/{id:guid}/retry` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/OperationsEndpoints.cs:66` |
| POST | `/operations/{id:guid}/cancel` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/OperationsEndpoints.cs:82` |
| GET | `/persons/{id:guid}/editor` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:27` |
| PUT | `/persons/{id:guid}/editor` | RequireAnyRole, RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:40` |
| GET | `/persons/{id:guid}/artwork` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:67` |
| POST | `/persons/{id:guid}/artwork/{assetType}` | RequireAnyRole, RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:122` |
| GET | `/persons/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:194` |
| GET | `/persons/{id:guid}/aliases` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:252` |
| GET | `/persons/{id:guid}/headshot` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:269` |
| GET | `/persons/by-collection/{collectionId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:361` |
| GET | `/persons/by-work/{workId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:372` |
| GET | `/persons/{id:guid}/library-credits` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:383` |
| GET | `/persons/{id:guid}/works` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:403` |
| GET | `/persons/role-counts` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:436` |
| GET | `/persons/presence` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:452` |
| GET | `/persons/` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PersonEndpoints.cs:467` |
| GET | `/api/v1/playback/{assetId:guid}/manifest` | RequireClientScope(ClientApiScopes.PlaybackRead) | Production | `src/MediaEngine.Api/Endpoints/PlaybackEndpoints.cs:34` |
| POST | `/api/v1/playback/{assetId:guid}/encode` | RequireClientScope(ClientApiScopes.DownloadsWrite) | Production | `src/MediaEngine.Api/Endpoints/PlaybackEndpoints.cs:79` |
| GET | `/api/v1/playback/encode/jobs` | RequireClientScope(ClientApiScopes.DownloadsRead) | Production | `src/MediaEngine.Api/Endpoints/PlaybackEndpoints.cs:106` |
| POST | `/api/v1/playback/encode/jobs/{jobId:guid}/cancel` | RequireClientScope(ClientApiScopes.DownloadsWrite) | Production | `src/MediaEngine.Api/Endpoints/PlaybackEndpoints.cs:126` |
| GET | `/api/v1/playback/diagnostics` | RequireClientScope(ClientApiScopes.PlaybackRead) | Production | `src/MediaEngine.Api/Endpoints/PlaybackEndpoints.cs:151` |
| GET | `/api/v1/playback/{assetId:guid}/offline/{variantId:guid}` | RequireClientScope(ClientApiScopes.DownloadsRead) | Production | `src/MediaEngine.Api/Endpoints/PlaybackEndpoints.cs:163` |
| GET | `/playback/{assetId:guid}/segments` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/PlaybackSegmentEndpoints.cs:16` |
| POST | `/playback/{assetId:guid}/segments/detect` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PlaybackSegmentEndpoints.cs:28` |
| PUT | `/playback/{assetId:guid}/segments/{segmentId:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PlaybackSegmentEndpoints.cs:41` |
| DELETE | `/playback/{assetId:guid}/segments/{segmentId:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PlaybackSegmentEndpoints.cs:66` |
| GET | `/api/v1/player/capabilities` | RequireClientScope(ClientApiScopes.PlaybackRead) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:22` |
| GET | `/api/v1/player/state` | RequireClientScope(ClientApiScopes.QueueRead) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:29` |
| POST | `/api/v1/player/queue/replace` | RequireClientScope(ClientApiScopes.QueueWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:46` |
| POST | `/api/v1/player/queue/items` | RequireClientScope(ClientApiScopes.QueueWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:71` |
| METHODS | `/api/v1/player/queue/order` | RequireClientScope(ClientApiScopes.QueueWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:97` |
| DELETE | `/api/v1/player/queue/items/{queueItemId:guid}` | RequireClientScope(ClientApiScopes.QueueWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:118` |
| DELETE | `/api/v1/player/queue` | RequireClientScope(ClientApiScopes.QueueWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:152` |
| POST | `/api/v1/player/command` | RequireClientScope(ClientApiScopes.PlaybackWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:185` |
| POST | `/api/v1/player/heartbeat` | RequireClientScope(ClientApiScopes.ProgressWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:199` |
| POST | `/api/v1/player/session/takeover` | RequireClientScope(ClientApiScopes.PlaybackWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:213` |
| GET | `/api/v1/player/audiobooks/{workId:guid}/history` | RequireClientScope(ClientApiScopes.ProgressRead) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:234` |
| GET | `/api/v1/player/audiobooks/{workId:guid}/bookmarks` | RequireClientScope(ClientApiScopes.ProgressRead) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:254` |
| POST | `/api/v1/player/audiobooks/{workId:guid}/bookmarks` | RequireClientScope(ClientApiScopes.ProgressWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:270` |
| DELETE | `/api/v1/player/audiobooks/bookmarks/{bookmarkId:guid}` | RequireClientScope(ClientApiScopes.ProgressWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:293` |
| GET | `/api/v1/player/audiobooks/{workId:guid}/chapter-overrides` | RequireClientScope(ClientApiScopes.LibraryRead) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:310` |
| POST | `/api/v1/player/audiobooks/{workId:guid}/chapter-overrides` | RequireClientScope(ClientApiScopes.ProgressWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:324` |
| DELETE | `/api/v1/player/audiobooks/{workId:guid}/chapter-overrides/{assetId:guid}/{chapterIndex:int}` | RequireClientScope(ClientApiScopes.ProgressWrite) | Production | `src/MediaEngine.Api/Endpoints/PlayerEndpoints.cs:351` |
| GET | `/plugins` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:19` |
| GET | `/plugins/approved` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:25` |
| GET | `/plugins/{pluginId}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:31` |
| POST | `/plugins/{pluginId}/enable` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:40` |
| POST | `/plugins/{pluginId}/disable` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:49` |
| PUT | `/plugins/{pluginId}/settings` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:58` |
| GET | `/plugins/{pluginId}/manifest` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:70` |
| PUT | `/plugins/{pluginId}/manifest` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:85` |
| DELETE | `/plugins/{pluginId}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:108` |
| POST | `/plugins/{pluginId}/health` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:124` |
| GET | `/plugins/{pluginId}/jobs` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:158` |
| POST | `/plugins/jobs/segment-detection/run` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/PluginEndpoints.cs:170` |
| GET | `/profiles/` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:41` |
| GET | `/profiles/{id:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:61` |
| GET | `/profiles/{id:guid}/taste` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:77` |
| GET | `/profiles/{id:guid}/overview` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:98` |
| GET | `/profiles/{id:guid}/settings/playback` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:117` |
| GET | `/profiles/{id:guid}/settings/view` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:138` |
| PUT | `/profiles/{id:guid}/settings/view` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:158` |
| PUT | `/profiles/{id:guid}/settings/playback` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:191` |
| GET | `/profiles/{id:guid}/sequence-preferences/missing-items` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:218` |
| PUT | `/profiles/{id:guid}/sequence-preferences/missing-items` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:250` |
| DELETE | `/profiles/{id:guid}/sequence-preferences/missing-items` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:285` |
| GET | `/profiles/{id:guid}/avatar` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:317` |
| POST | `/profiles/{id:guid}/avatar` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:342` |
| DELETE | `/profiles/{id:guid}/avatar` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:351` |
| POST | `/profiles/` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:383` |
| METHODS | `/profiles/{id:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:431` |
| DELETE | `/profiles/{id:guid}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ProfileEndpoints.cs:474` |
| GET | `/api/v1/progress/{assetId:guid}` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.ProgressRead) | Production | `src/MediaEngine.Api/Endpoints/ProgressEndpoints.cs:24` |
| PUT | `/api/v1/progress/{assetId:guid}` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.ProgressWrite) | Production | `src/MediaEngine.Api/Endpoints/ProgressEndpoints.cs:39` |
| GET | `/api/v1/progress/recent` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.ProgressRead) | Production | `src/MediaEngine.Api/Endpoints/ProgressEndpoints.cs:69` |
| GET | `/api/v1/progress/journey` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.ProgressRead) | Production | `src/MediaEngine.Api/Endpoints/ProgressEndpoints.cs:83` |
| GET | `/api/v1/progress/status/{entityType}/{targetId:guid}` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.ProgressRead) | Production | `src/MediaEngine.Api/Endpoints/ProgressEndpoints.cs:100` |
| POST | `/api/v1/progress/status` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.ProgressWrite) | Production | `src/MediaEngine.Api/Endpoints/ProgressEndpoints.cs:109` |
| POST | `/api/v1/progress/status/{commandId:guid}/undo` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.ProgressWrite) | Production | `src/MediaEngine.Api/Endpoints/ProgressEndpoints.cs:120` |
| GET | `/api/v1/progress/status/{entityType}/{targetId:guid}/history` | RequireAuthorization(AuthPolicies.Authenticated), RequireClientScope(ClientApiScopes.ProgressRead) | Production | `src/MediaEngine.Api/Endpoints/ProgressEndpoints.cs:128` |
| GET | `/providers/catalogue` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ProviderCatalogueEndpoints.cs:30` |
| GET | `/read/{assetId:guid}/metadata` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReadEndpoints.cs:27` |
| GET | `/read/{assetId:guid}/toc` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReadEndpoints.cs:59` |
| GET | `/read/{assetId:guid}/chapter/{index:int}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReadEndpoints.cs:85` |
| GET | `/read/{assetId:guid}/resource/{**path}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReadEndpoints.cs:127` |
| GET | `/read/{assetId:guid}/search` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReadEndpoints.cs:162` |
| GET | `/read/resolve/{workId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReadEndpoints.cs:197` |
| GET | `/reader/{assetId:guid}/bookmarks` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:25` |
| POST | `/reader/{assetId:guid}/bookmarks` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:38` |
| DELETE | `/reader/bookmarks/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:63` |
| GET | `/reader/{assetId:guid}/highlights` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:85` |
| POST | `/reader/{assetId:guid}/highlights` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:98` |
| PUT | `/reader/highlights/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:126` |
| DELETE | `/reader/highlights/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:147` |
| GET | `/reader/{assetId:guid}/statistics` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:169` |
| PUT | `/reader/{assetId:guid}/statistics` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ReaderEndpoints.cs:192` |
| POST | `/reports/` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReportEndpoints.cs:20` |
| GET | `/reports/entity/{entityId:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReportEndpoints.cs:59` |
| POST | `/reports/{activityId:long}/resolve` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReportEndpoints.cs:87` |
| POST | `/reports/{activityId:long}/dismiss` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReportEndpoints.cs:110` |
| GET | `/review/pending` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReviewEndpoints.cs:34` |
| GET | `/review/count` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReviewEndpoints.cs:49` |
| GET | `/review/{id:guid}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReviewEndpoints.cs:62` |
| POST | `/review/{id:guid}/resolve` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReviewEndpoints.cs:77` |
| POST | `/review/{id:guid}/dismiss` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReviewEndpoints.cs:218` |
| POST | `/review/{id:guid}/skip-universe` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/ReviewEndpoints.cs:283` |
| POST | `/search/universe` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SearchEndpoints.cs:24` |
| POST | `/search/retail/detail` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SearchEndpoints.cs:41` |
| POST | `/search/retail` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SearchEndpoints.cs:101` |
| POST | `/search/resolve` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SearchEndpoints.cs:127` |
| GET | `/settings/server-folders/roots` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ServerFolderEndpoints.cs:17` |
| POST | `/settings/server-folders/browse` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ServerFolderEndpoints.cs:23` |
| POST | `/settings/server-folders/validate` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ServerFolderEndpoints.cs:41` |
| GET | `/settings/security/auth` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:104` |
| GET | `/settings/transcoding` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:136` |
| PUT | `/settings/transcoding` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:145` |
| GET | `/settings/libraries` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:196` |
| PUT | `/settings/libraries` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:205` |
| POST | `/settings/test-path` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:246` |
| GET | `/settings/providers/health` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:300` |
| PUT | `/settings/providers/{name}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:323` |
| GET | `/settings/providers` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:350` |
| GET | `/settings/organization-template` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:385` |
| POST | `/settings/organization-template/preview` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:411` |
| PUT | `/settings/organization-template` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:458` |
| POST | `/settings/providers/{name}/credentials/test` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:516` |
| PUT | `/settings/providers/{name}/credentials` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:529` |
| DELETE | `/settings/providers/{name}/credentials` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:542` |
| POST | `/settings/providers/{name}/test` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:557` |
| POST | `/settings/providers/{name}/sample` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:678` |
| PUT | `/settings/providers/{name}/config` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:760` |
| DELETE | `/settings/providers/{name}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:862` |
| GET | `/settings/hydration` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:901` |
| PUT | `/settings/hydration` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:912` |
| GET | `/settings/pipelines` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:925` |
| GET | `/settings/pipelines/defaults` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:935` |
| PUT | `/settings/pipelines` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:954` |
| GET | `/settings/media-types` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:967` |
| PUT | `/settings/media-types` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:979` |
| POST | `/settings/media-types/add` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:1014` |
| DELETE | `/settings/media-types/{key}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:1049` |
| POST | `/settings/providers/{name}/icon` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:1081` |
| GET | `/settings/providers/{name}/icon` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:1133` |
| GET | `/settings/server-general` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:1171` |
| PUT | `/settings/server-general` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:1194` |
| GET | `/settings/catalog` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/SettingsEndpoints.cs:1224` |
| GET | `/setup/v1/status` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:24` |
| POST | `/setup/v1/begin` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:28` |
| POST | `/setup/v1/preflight` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:37` |
| POST | `/setup/v1/administrator` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:50` |
| POST | `/setup/v1/media-locations/validate` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:76` |
| GET | `/setup/v1/libraries` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:88` |
| PUT | `/setup/v1/libraries` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:96` |
| GET | `/setup/v1/server-folders/roots` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:115` |
| POST | `/setup/v1/server-folders/browse` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:123` |
| POST | `/setup/v1/server-folders/validate` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:132` |
| PUT | `/setup/v1/providers/{name}/credentials` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:141` |
| POST | `/setup/v1/providers/{name}/credentials/test` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:150` |
| POST | `/setup/v1/steps/{stepKey}` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:159` |
| POST | `/setup/v1/restore/upload` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:176` |
| POST | `/setup/v1/restore/{operationId:guid}/confirm` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:198` |
| GET | `/setup/v1/readiness` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:222` |
| POST | `/setup/v1/complete` | RequireAuthorization(AuthPolicies.DashboardService) | Production | `src/MediaEngine.Api/Endpoints/SetupEndpoints.cs:230` |
| GET | `/stream/{assetId:guid}` | RequireClientScope(ClientApiScopes.PlaybackRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:63` |
| GET | `/stream/artwork/{variantId:guid}` | RequireClientScope(ClientApiScopes.ArtworkRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:119` |
| GET | `/stream/entity/{entityType}/{entityId:guid}/cover` | RequireClientScope(ClientApiScopes.ArtworkRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:178` |
| GET | `/stream/{assetId:guid}/cover` | RequireClientScope(ClientApiScopes.ArtworkRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:238` |
| GET | `/stream/{assetId:guid}/text-tracks` | RequireClientScope(ClientApiScopes.PlaybackRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:268` |
| GET | `/stream/{assetId:guid}/text-tracks/{trackId:guid}` | RequireClientScope(ClientApiScopes.PlaybackRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:302` |
| POST | `/stream/{assetId:guid}/text-tracks/{trackId:guid}/preferred` | RequireClientScope(ClientApiScopes.PlaybackWrite) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:326` |
| POST | `/stream/{assetId:guid}/text-tracks/import` | RequireClientScope(ClientApiScopes.PlaybackWrite) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:350` |
| GET | `/stream/{assetId:guid}/lyrics` | RequireClientScope(ClientApiScopes.PlaybackRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:400` |
| GET | `/stream/{assetId:guid}/subtitles` | RequireClientScope(ClientApiScopes.PlaybackRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:418` |
| POST | `/stream/{assetId:guid}/text-tracks/refresh` | RequireClientScope(ClientApiScopes.PlaybackWrite) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:437` |
| GET | `/stream/{assetId:guid}/cover-thumb` | RequireClientScope(ClientApiScopes.ArtworkRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:513` |
| GET | `/stream/{assetId:guid}/background` | RequireClientScope(ClientApiScopes.ArtworkRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:543` |
| GET | `/stream/{assetId:guid}/logo` | RequireClientScope(ClientApiScopes.ArtworkRead) | Production | `src/MediaEngine.Api/Endpoints/StreamEndpoints.cs:571` |
| GET | `/system/status` | AllowAnonymous | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:32` |
| GET | `/system/readiness` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:48` |
| GET | `/system/activity-status` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:57` |
| GET | `/system/watcher-status` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:79` |
| POST | `/maintenance/sweep-orphan-assets` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:96` |
| GET | `/system/backups` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:109` |
| POST | `/system/backups` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:117` |
| GET | `/system/backups/{fileName}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:133` |
| POST | `/system/backups/validate` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:157` |
| POST | `/system/backups/restore` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/SystemEndpoints.cs:188` |
| GET | `/timeline/{entityId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/TimelineEndpoints.cs:28` |
| GET | `/timeline/{entityId:guid}/pipeline` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/TimelineEndpoints.cs:41` |
| GET | `/timeline/{entityId:guid}/event/{eventId:guid}/changes` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/TimelineEndpoints.cs:54` |
| POST | `/timeline/{entityId:guid}/rematch` | RequireAnyRole, RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/TimelineEndpoints.cs:68` |
| GET | `/settings/ui/global` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:59` |
| PUT | `/settings/ui/global` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:72` |
| GET | `/settings/ui/device/{deviceClass}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:89` |
| PUT | `/settings/ui/device/{deviceClass}` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:111` |
| GET | `/settings/ui/profile/{profileId}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:136` |
| PUT | `/settings/ui/profile/{profileId}` | RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:154` |
| GET | `/settings/ui/resolved` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:175` |
| GET | `/settings/ui/library-preferences` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:197` |
| GET | `/settings/ui/library-preferences/diagnostics` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:208` |
| PUT | `/settings/ui/library-preferences` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UISettingsEndpoints.cs:228` |
| GET | `//universes` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:28` |
| GET | `//universe/{qid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:69` |
| GET | `//universe/{qid}/health` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:95` |
| GET | `//universe/{qid}/lore-delta` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:138` |
| GET | `//universe/{qid}/graph` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:149` |
| POST | `//universe/entity/{qid}/deep-enrich` | RequireAnyRole, RequireAdminOrStandardUser | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:307` |
| GET | `//universe/{qid}/paths` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:410` |
| GET | `//universe/{qid}/family-tree` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:424` |
| GET | `//universe/{qid}/cross-media` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:442` |
| GET | `//universe/{qid}/cast` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:453` |
| GET | `//universe/{qid}/adaptations` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/UniverseGraphEndpoints.cs:500` |
| GET | `/universe/{qid}/lore-sources` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UniverseLoreEndpoints.cs:17` |
| POST | `/universe/{qid}/lore-sources/discover` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UniverseLoreEndpoints.cs:29` |
| POST | `/universe/{qid}/lore-sources/manual` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UniverseLoreEndpoints.cs:41` |
| POST | `/universe/{qid}/lore-sources/{sourceId:guid}/approve` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UniverseLoreEndpoints.cs:69` |
| POST | `/universe/{qid}/lore-sources/{sourceId:guid}/reject` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UniverseLoreEndpoints.cs:94` |
| POST | `/universe/{qid}/lore/enrich` | RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/UniverseLoreEndpoints.cs:119` |
| GET | `/view/places` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewDiscoveryEndpoints.cs:16` |
| GET | `/view/people` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewDiscoveryEndpoints.cs:47` |
| GET | `/view/scopes` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:22` |
| GET | `/view/preferences` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:38` |
| PUT | `/view/preferences` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:45` |
| GET | `/view/assets` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:73` |
| GET | `/view/folders` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:97` |
| PUT | `/view/folders/pin` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:118` |
| PUT | `/view/folders/timeline-policy` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:139` |
| POST | `/view/uploads` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:162` |
| GET | `/view/items/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:195` |
| GET | `/view/items/{id:guid}/content` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:206` |
| GET | `/view/items/{id:guid}/thumbnail` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:228` |
| POST | `/view/shared/contributions/preview` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:253` |
| POST | `/view/shared/contributions` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:264` |
| GET | `/view/shared/contributions` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:275` |
| GET | `/view/shared/contributions/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:286` |
| POST | `/view/shared/contributions/{id:guid}/cancel` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:295` |
| POST | `/view/shared/contributions/{id:guid}/decision` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:306` |
| POST | `/view/shared/contributions/{id:guid}/retry` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:318` |
| POST | `/view/shared/items/direct` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:329` |
| GET | `/view/share-targets` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:342` |
| GET | `/view/admin/profiles/{profileId:guid}/sources` | RequireAnyRole, RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:353` |
| POST | `/view/admin/profiles/{profileId:guid}/sources` | RequireAnyRole, RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:379` |
| PUT | `/view/admin/profiles/{profileId:guid}/sources/{sourceId:guid}` | RequireAnyRole, RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:425` |
| DELETE | `/view/admin/profiles/{profileId:guid}/sources/{sourceId:guid}` | RequireAnyRole, RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:441` |
| POST | `/view/admin/profiles/{profileId:guid}/reconcile` | RequireAnyRole, RequireAdmin | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:457` |
| GET | `/view/galleries` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:472` |
| POST | `/view/galleries` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:485` |
| GET | `/view/galleries/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:510` |
| PUT | `/view/galleries/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:519` |
| DELETE | `/view/galleries/{id:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:539` |
| GET | `/view/galleries/{id:guid}/items` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:549` |
| POST | `/view/galleries/{id:guid}/items` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:560` |
| DELETE | `/view/galleries/{id:guid}/items` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:569` |
| PUT | `/view/galleries/{id:guid}/items/{itemId:guid}/position` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:579` |
| GET | `/view/galleries/{id:guid}/shares` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:591` |
| PUT | `/view/galleries/{id:guid}/shares` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:600` |
| PUT | `/view/items/{{id:guid}}/{name}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:677` |
| POST | `/view/items/{{id:guid}}/{name}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/ViewEndpoints.cs:688` |
| GET | `/works/{workId:guid}` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/WorkEndpoints.cs:16` |
| GET | `/works/{workId:guid}/editions` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/WorkEndpoints.cs:30` |
| GET | `/works/{workId:guid}/cast` | RequireAnyRole | Production | `src/MediaEngine.Api/Endpoints/WorkEndpoints.cs:44` |
| HEALTH | `/health` | RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Program.cs:385` |
| HEALTH | `/health/live` | AllowAnonymous | Production | `src/MediaEngine.Api/Program.cs:386` |
| HEALTH | `/health/ready` | RequireAuthorization(AuthPolicies.Administrator) | Production | `src/MediaEngine.Api/Program.cs:390` |
| GET | `/.well-known/tuvima` | AllowAnonymous | Production | `src/MediaEngine.Web/Endpoints/ClientApiEdgeEndpoints.cs:15` |
| GET | `/pair` | fallback policy / middleware | Production | `src/MediaEngine.Web/Endpoints/ClientApiEdgeEndpoints.cs:25` |
| POST | `/pair` | fallback policy / middleware | Production | `src/MediaEngine.Web/Endpoints/ClientApiEdgeEndpoints.cs:49` |
| GET/HEAD/POST/PUT/PATCH/DELETE | `/api/v1/{**clientPath}` | AllowAnonymous | Production | `src/MediaEngine.Web/Endpoints/ClientApiEdgeEndpoints.cs:76` |
| HEALTH | `/health/live` | AllowAnonymous | Production | `src/MediaEngine.Web/Program.cs:336` |
| HEALTH | `/health/ready` | AllowAnonymous | Production | `src/MediaEngine.Web/Program.cs:340` |
| GET | `/_tuvima/remote-probe` | AllowAnonymous | Production | `src/MediaEngine.Web/Program.cs:350` |
| GET | `/culture/set` | AllowAnonymous | Production | `src/MediaEngine.Web/Program.cs:360` |
| GET/HEAD | `/engine-stream/{assetId:guid}` | fallback policy / middleware | Production | `src/MediaEngine.Web/Program.cs:372` |
| GET/HEAD | `/engine-hls/{grant}/{packageId:guid}/{**resourcePath}` | AllowAnonymous | Production | `src/MediaEngine.Web/Program.cs:376` |
| GET/HEAD | `/engine-image/{**enginePath}` | RequireAuthorization | Production | `src/MediaEngine.Web/Program.cs:384` |
| GET | `/auth/external/{providerId}` | AllowAnonymous | Production | `src/MediaEngine.Web/Program.cs:396` |
| GET | `/auth/login` | AllowAnonymous | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:18` |
| POST | `/auth/login` | AllowAnonymous | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:44` |
| GET | `/auth/reset` | AllowAnonymous | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:130` |
| POST | `/auth/reset` | AllowAnonymous | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:137` |
| GET | `/auth/invite` | AllowAnonymous | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:144` |
| POST | `/auth/invite` | AllowAnonymous | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:149` |
| POST | `/auth/passkeys/login/options` | AllowAnonymous | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:155` |
| POST | `/auth/passkeys/login/complete` | AllowAnonymous | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:158` |
| POST | `/auth/passkeys/registration/options` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:165` |
| POST | `/auth/passkeys/registration/complete` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:168` |
| POST | `/auth/passkeys/elevation/options` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:171` |
| POST | `/auth/passkeys/elevation/complete` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:173` |
| POST | `/auth/logout` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:176` |
| GET | `/account/elevate` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:188` |
| POST | `/account/elevate` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:195` |
| GET | `/account/security` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:202` |
| POST | `/account/security` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/DashboardAuthenticationEndpoints.cs:213` |
| GET/HEAD | `/view-media/{grant}` | fallback policy / middleware | Production | `src/MediaEngine.Web/Services/Integration/ViewMediaProxyEndpoint.cs:6` |

