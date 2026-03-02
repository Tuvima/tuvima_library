using Tanaste.Api.Hubs;
using Tanaste.Api.Models;
using Tanaste.Api.Security;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Entities;
using Tanaste.Domain.Enums;
using Tanaste.Intelligence.Contracts;
using Tanaste.Intelligence.Models;
using Tanaste.Providers.Adapters;
using Tanaste.Providers.Contracts;
using Tanaste.Providers.Models;
using Tanaste.Storage.Contracts;

namespace Tanaste.Api.Endpoints;

public static class MetadataEndpoints
{
    /// <summary>Well-known provider GUID for user-manual metadata corrections.</summary>
    private static readonly Guid UserManualProviderId =
        new("d0000000-0000-4000-8000-000000000001");

    public static IEndpointRouteBuilder MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/metadata")
                       .WithTags("Metadata");

        // ── GET /metadata/claims/{entityId} ──────────────────────────────────
        group.MapGet("/claims/{entityId:guid}", async (
            Guid entityId,
            IMetadataClaimRepository claimRepo,
            CancellationToken ct) =>
        {
            var claims = await claimRepo.GetByEntityAsync(entityId, ct);
            var dtos = claims.Select(ClaimDto.FromDomain).ToList();
            return Results.Ok(dtos);
        })
        .WithName("GetClaimHistory")
        .WithSummary("Returns all metadata claims for a Work or Edition, ordered by claimed_at.")
        .Produces<List<ClaimDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // ── GET /metadata/conflicts ─────────────────────────────────────────
        group.MapGet("/conflicts", async (
            ICanonicalValueRepository canonicalRepo,
            CancellationToken ct) =>
        {
            var conflicted = await canonicalRepo.GetConflictedAsync(ct);
            var dtos = conflicted.Select(ConflictDto.FromDomain).ToList();
            return Results.Ok(dtos);
        })
        .WithName("GetConflicts")
        .WithSummary("Returns all canonical values with unresolved metadata conflicts.")
        .Produces<List<ConflictDto>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── PATCH /metadata/lock-claim ───────────────────────────────────────
        group.MapMethods("/lock-claim", ["PATCH"], async (
            LockClaimRequest request,
            IMetadataClaimRepository claimRepo,
            IDatabaseConnection db,
            ITransactionJournal journal,
            IEventPublisher publisher,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(request.ClaimKey))
                return Results.BadRequest("claim_key must not be empty.");
            if (string.IsNullOrWhiteSpace(request.ChosenValue))
                return Results.BadRequest("chosen_value must not be empty.");

            var lockedAt = DateTimeOffset.UtcNow;

            // 1. Insert a user-locked claim (confidence 1.0).
            var claim = new MetadataClaim
            {
                Id           = Guid.NewGuid(),
                EntityId     = request.EntityId,
                ProviderId   = UserManualProviderId,
                ClaimKey     = request.ClaimKey,
                ClaimValue   = request.ChosenValue,
                Confidence   = 1.0,
                ClaimedAt    = lockedAt,
                IsUserLocked = true,
            };
            await claimRepo.InsertBatchAsync([claim], ct);

            // 2. Upsert the canonical value so the Dashboard sees the change immediately.
            //    User-locked claims resolve any conflict, so is_conflicted is set to 0.
            var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted)
                VALUES (@entity_id, @key, @value, @last_scored_at, 0)
                ON CONFLICT(entity_id, key) DO UPDATE SET
                    value          = excluded.value,
                    last_scored_at = excluded.last_scored_at,
                    is_conflicted  = 0;
                """;
            cmd.Parameters.AddWithValue("@entity_id",      request.EntityId.ToString());
            cmd.Parameters.AddWithValue("@key",            request.ClaimKey);
            cmd.Parameters.AddWithValue("@value",          request.ChosenValue);
            cmd.Parameters.AddWithValue("@last_scored_at", lockedAt.ToString("O"));
            cmd.ExecuteNonQuery();

            // 3. Audit trail.
            journal.Log("CLAIM_USER_LOCKED", "MetadataClaim", request.EntityId.ToString());

            // 4. Broadcast so the Dashboard refreshes.
            await publisher.PublishAsync("MetadataHarvested", new
            {
                entity_id     = request.EntityId,
                provider_name = "user_manual",
                updated_fields = new[] { request.ClaimKey },
            });

            return Results.Ok(new LockClaimResponse
            {
                EntityId    = request.EntityId,
                ClaimKey    = request.ClaimKey,
                ChosenValue = request.ChosenValue,
                LockedAt    = lockedAt,
            });
        })
        .WithName("LockClaim")
        .WithSummary("Create a user-locked metadata claim and update the canonical value. Used by the Curator's Drawer.")
        .Produces<LockClaimResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── PATCH /metadata/resolve (legacy) ─────────────────────────────────
        group.MapMethods("/resolve", ["PATCH"], async (
            ResolveRequest request,
            IDatabaseConnection db,
            ITransactionJournal journal,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(request.ClaimKey))
                return Results.BadRequest("claim_key must not be empty.");

            if (string.IsNullOrWhiteSpace(request.ChosenValue))
                return Results.BadRequest("chosen_value must not be empty.");

            var resolvedAt = DateTimeOffset.UtcNow;

            var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted)
                VALUES (@entity_id, @key, @value, @last_scored_at, 0)
                ON CONFLICT(entity_id, key) DO UPDATE SET
                    value          = excluded.value,
                    last_scored_at = excluded.last_scored_at,
                    is_conflicted  = 0;
                """;
            cmd.Parameters.AddWithValue("@entity_id",      request.EntityId.ToString());
            cmd.Parameters.AddWithValue("@key",            request.ClaimKey);
            cmd.Parameters.AddWithValue("@value",          request.ChosenValue);
            cmd.Parameters.AddWithValue("@last_scored_at", resolvedAt.ToString("O"));
            cmd.ExecuteNonQuery();

            journal.Log(
                "CANONICAL_VALUE_MANUAL_RESOLVE",
                "CanonicalValue",
                request.EntityId.ToString());

            return Results.Ok(new ResolveResponse
            {
                EntityId    = request.EntityId,
                ClaimKey    = request.ClaimKey,
                ChosenValue = request.ChosenValue,
                ResolvedAt  = resolvedAt,
            });
        })
        .WithName("ResolveMetadataConflict")
        .WithSummary("Manually override a metadata canonical value, locking in the chosen value.")
        .Produces<ResolveResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── POST /metadata/hydrate/{entityId} ─────────────────────────────
        group.MapPost("/hydrate/{entityId:guid}", async (
            Guid entityId,
            ICanonicalValueRepository canonicalRepo,
            IMetadataClaimRepository claimRepo,
            IScoringEngine scoringEngine,
            IEnumerable<IExternalMetadataProvider> providers,
            IConfigurationLoader configLoader,
            IEventPublisher publisher,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {
            // 1. Find the Wikidata adapter.
            var wikidataAdapter = providers.OfType<WikidataAdapter>().FirstOrDefault();
            if (wikidataAdapter is null)
            {
                return Results.Ok(new HydrateResponse
                {
                    Success = false,
                    Message = "Wikidata adapter is not registered.",
                });
            }

            // 2. Load existing canonical values to build lookup hints.
            var canonicals = await canonicalRepo.GetByEntityAsync(entityId, ct);
            var hints = canonicals.ToDictionary(c => c.Key, c => c.Value);

            // 3. Resolve the base URLs from wikidata provider config.
            var wikidataConfig = configLoader.LoadProvider("wikidata");
            var apiBaseUrl = wikidataConfig?.Endpoints
                .GetValueOrDefault("wikidata_api", string.Empty) ?? string.Empty;
            var sparqlBaseUrl = wikidataConfig?.Endpoints
                .GetValueOrDefault("wikidata_sparql");

            // 4. Build the lookup request.
            var lookupRequest = new ProviderLookupRequest
            {
                EntityId     = entityId,
                EntityType   = EntityType.Work,
                MediaType    = Domain.Enums.MediaType.Unknown,
                Title        = hints.GetValueOrDefault("title"),
                Author       = hints.GetValueOrDefault("author"),
                Narrator     = hints.GetValueOrDefault("narrator"),
                Asin         = hints.GetValueOrDefault("asin"),
                Isbn         = hints.GetValueOrDefault("isbn"),
                AppleBooksId = hints.GetValueOrDefault("apple_books_id"),
                AudibleId    = hints.GetValueOrDefault("audible_id"),
                TmdbId       = hints.GetValueOrDefault("tmdb_id"),
                ImdbId       = hints.GetValueOrDefault("imdb_id"),
                BaseUrl      = apiBaseUrl,
                SparqlBaseUrl = sparqlBaseUrl,
            };

            // 5. Call the Wikidata adapter directly (synchronous — user-triggered).
            IReadOnlyList<ProviderClaim> providerClaims;
            try
            {
                providerClaims = await wikidataAdapter.FetchAsync(lookupRequest, ct);
            }
            catch (Exception ex)
            {
                return Results.Ok(new HydrateResponse
                {
                    Success = false,
                    Message = $"Wikidata lookup failed: {ex.Message}",
                });
            }

            if (providerClaims.Count == 0)
            {
                return Results.Ok(new HydrateResponse
                {
                    Success = true,
                    ClaimsAdded = 0,
                    Message = "No matching Wikidata entity found for this work.",
                });
            }

            // 6. Persist claims (append-only).
            var domainClaims = providerClaims
                .Select(pc => new MetadataClaim
                {
                    Id           = Guid.NewGuid(),
                    EntityId     = entityId,
                    ProviderId   = WikidataAdapter.AdapterProviderId,
                    ClaimKey     = pc.Key,
                    ClaimValue   = pc.Value,
                    Confidence   = pc.Confidence,
                    ClaimedAt    = DateTimeOffset.UtcNow,
                    IsUserLocked = false,
                })
                .ToList();
            await claimRepo.InsertBatchAsync(domainClaims, ct);

            // 7. Re-score entity.
            var allClaims = await claimRepo.GetByEntityAsync(entityId, ct);
            var scoring = configLoader.LoadScoring();
            var scoringConfig = new ScoringConfiguration
            {
                AutoLinkThreshold     = scoring.AutoLinkThreshold,
                ConflictThreshold     = scoring.ConflictThreshold,
                ConflictEpsilon       = scoring.ConflictEpsilon,
                StaleClaimDecayDays   = scoring.StaleClaimDecayDays,
                StaleClaimDecayFactor = scoring.StaleClaimDecayFactor,
            };

            // Build provider weights from provider configs (match by name, key by ProviderId).
            var allProviderConfigs = configLoader.LoadAllProviders();
            var providerWeights = new Dictionary<Guid, double>();
            Dictionary<Guid, IReadOnlyDictionary<string, double>>? fieldWeights = null;
            foreach (var prov in providers)
            {
                var provConfig = allProviderConfigs
                    .FirstOrDefault(b => string.Equals(b.Name, prov.Name,
                        StringComparison.OrdinalIgnoreCase));
                if (provConfig is null) continue;

                providerWeights[prov.ProviderId] = provConfig.Weight;
                if (provConfig.FieldWeights.Count > 0)
                {
                    fieldWeights ??= new();
                    fieldWeights[prov.ProviderId] = (IReadOnlyDictionary<string, double>)provConfig.FieldWeights;
                }
            }

            var scoringContext = new ScoringContext
            {
                EntityId             = entityId,
                Claims               = allClaims,
                ProviderWeights      = providerWeights,
                ProviderFieldWeights = fieldWeights,
                Configuration        = scoringConfig,
            };
            var scored = await scoringEngine.ScoreEntityAsync(scoringContext, ct);

            // 8. Upsert canonical values.
            var newCanonicals = scored.FieldScores
                .Where(f => !string.IsNullOrEmpty(f.WinningValue))
                .Select(f => new CanonicalValue
                {
                    EntityId     = entityId,
                    Key          = f.Key,
                    Value        = f.WinningValue!,
                    LastScoredAt = scored.ScoredAt,
                    IsConflicted = f.IsConflicted,
                })
                .ToList();
            await canonicalRepo.UpsertBatchAsync(newCanonicals, ct);

            // 9. Log to activity ledger.
            var qid = providerClaims.FirstOrDefault(c => c.Key == "wikidata_qid")?.Value;
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType  = SystemActionType.MetadataHydrated,
                EntityId    = entityId,
                Detail      = $"Deep-hydrated via Wikidata SPARQL — QID: {qid ?? "unknown"}, {domainClaims.Count} claims added.",
                ChangesJson = $"{{\"qid\":\"{qid}\",\"claims\":{domainClaims.Count}}}",
                OccurredAt  = DateTimeOffset.UtcNow,
            }, ct);

            // 10. Broadcast MetadataHarvested event.
            var updatedFields = domainClaims.Select(c => c.ClaimKey).Distinct().ToList();
            await publisher.PublishAsync("MetadataHarvested", new
            {
                entity_id      = entityId,
                provider_name  = "wikidata",
                updated_fields = updatedFields,
            }, ct);

            return Results.Ok(new HydrateResponse
            {
                WikidataQid = qid,
                ClaimsAdded = domainClaims.Count,
                Success     = true,
                Message     = $"Hydrated {domainClaims.Count} claims from Wikidata (QID: {qid ?? "unknown"}).",
            });
        })
        .WithName("HydrateEntity")
        .WithSummary("Trigger Wikidata SPARQL deep hydration for a Work or Edition entity. Admin or Curator.")
        .Produces<HydrateResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
    }
}
