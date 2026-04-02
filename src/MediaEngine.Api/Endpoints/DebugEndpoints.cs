using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Adapters;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Debug/test endpoints for validating the Wikidata enrichment pipeline.
/// These endpoints do NOT persist any data to the database — they are for
/// testing and validation only.
/// </summary>
public static class DebugEndpoints
{
    public static IEndpointRouteBuilder MapDebugEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/debug")
                       .WithTags("Debug");

        // ── POST /debug/lookup ────────────────────────────────────────────────
        // Live Wikidata Reconciliation + Data Extension lookup.
        // Returns all enrichment data for the given title without persisting anything.
        group.MapPost("/lookup", async (
            DebugLookupRequest request,
            IEnumerable<IExternalMetadataProvider> providers,
            IPersonRepository personRepo,
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            IConfigurationLoader configLoader,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest("Title is required.");

            // Find the Wikidata Reconciliation provider.
            var provider = providers.FirstOrDefault(p => p.Domain == ProviderDomain.Universal);
            if (provider is null)
                return Results.Problem(
                    "Wikidata Reconciliation provider is not registered.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            // Parse media type — default to Unknown if unrecognised.
            Enum.TryParse<MediaType>(request.MediaType, ignoreCase: true, out var mediaType);

            // Load core config for language/country.
            var core = configLoader.LoadCore();

            var lookupRequest = new ProviderLookupRequest
            {
                EntityId     = Guid.NewGuid(), // ephemeral — not persisted
                EntityType   = EntityType.MediaAsset,
                MediaType    = mediaType,
                Title        = request.Title,
                Author       = request.Author,
                Language     = core.Language.Metadata,
                Country      = core.Country  ?? "us",
                HydrationPass = HydrationPass.Universe,
                BaseUrl      = string.Empty,
            };

            IReadOnlyList<ProviderClaim> claims;
            try
            {
                claims = await provider.FetchAsync(lookupRequest, ct);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    $"Provider returned an error: {ex.Message}",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var providerId = provider.ProviderId.ToString();

            // ── Extract resolved QID ─────────────────────────────────────────
            var qidClaim = claims.FirstOrDefault(c =>
                string.Equals(c.Key, "wikidata_qid", StringComparison.OrdinalIgnoreCase));
            var resolvedQid = qidClaim?.Value;

            // ── Group claims by field key ─────────────────────────────────────
            var claimGroups = claims
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .Select(g => new DebugClaimGroup(
                    g.Key,
                    g.Select(c => new DebugClaimEntry(c.Value, c.Confidence, providerId))
                      .ToList()))
                .ToList();

            // ── Resolve persons from companion _qid claims ────────────────────
            // Companion _qid claims follow the pattern "{field}_qid" with value "Q12345::Label".
            // We look for author_qid, narrator_qid, director_qid, cast_qid, etc.
            var personQids = ExtractCompanionQids(claims,
                "author_qid", "narrator_qid", "director_qid", "cast_qid",
                "screenwriter_qid", "composer_qid", "illustrator_qid");

            var persons = new List<DebugPersonResult>();
            foreach (var (qid, _) in personQids)
            {
                var person = await personRepo.FindByQidAsync(qid, ct);
                if (person is not null)
                {
                    persons.Add(new DebugPersonResult(
                        person.Name,
                        string.Join(", ", person.Roles),
                        person.WikidataQid,
                        person.HeadshotUrl,
                        person.Biography,
                        person.Occupation));
                }
            }

            // ── Resolve fictional entities from companion _qid claims ──────────
            // Character, location, organisation QIDs come from claims like
            // "character_qid", "location_qid", "organization_qid", etc.
            var entityQids = ExtractCompanionQids(claims,
                "character_qid", "location_qid", "organization_qid",
                "fictional_entity_qid", "cast_member_qid");

            var fictionalEntities = new List<DebugEntityResult>();
            var entityLabelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (qid, label) in entityQids)
            {
                entityLabelMap.TryAdd(qid, label);
                var entity = await entityRepo.FindByQidAsync(qid, ct);
                if (entity is not null)
                {
                    fictionalEntities.Add(new DebugEntityResult(
                        entity.Label,
                        entity.WikidataQid,
                        entity.EntitySubType,
                        entity.Description,
                        entity.ImageUrl));
                    // Prefer the DB label for relationship resolution.
                    entityLabelMap[qid] = entity.Label;
                }
            }

            // ── Resolve relationships for any entities found ───────────────────
            var relationships = new List<DebugRelationshipResult>();
            var allEntityQidStrings = entityQids.Select(e => e.Qid).ToList();
            if (allEntityQidStrings.Count > 0)
            {
                var edges = await relRepo.GetByUniverseAsync(
                    allEntityQidStrings.ToHashSet(StringComparer.OrdinalIgnoreCase), ct);

                foreach (var edge in edges)
                {
                    entityLabelMap.TryGetValue(edge.SubjectQid, out var subjectLabel);
                    entityLabelMap.TryGetValue(edge.ObjectQid, out var objectLabel);

                    relationships.Add(new DebugRelationshipResult(
                        edge.SubjectQid,
                        subjectLabel ?? edge.SubjectQid,
                        edge.RelationshipTypeValue,
                        edge.ObjectQid,
                        objectLabel ?? edge.ObjectQid,
                        edge.StartTime,
                        edge.EndTime));
                }
            }

            var allProviderConfigs = configLoader.LoadAllProviders();
            var bridgeHintPreview = ComputeBridgeHintPreview(claims, mediaType.ToString(), allProviderConfigs);

            var response = new DebugLookupResponse(
                resolvedQid,
                claimGroups,
                persons,
                fictionalEntities,
                relationships,
                bridgeHintPreview);

            return Results.Ok(response);
        })
        .WithName("DebugLookup")
        .WithSummary("Live Wikidata lookup for a title + media type. Returns all enrichment claims without persisting anything.")
        .Produces<DebugLookupResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .RequireAdmin();

        // ── POST /debug/search ──────────────────────────────────────────────────
        // Step 1: Reconciliation only — returns candidate list for user selection.
        group.MapPost("/search", async (
            DebugLookupRequest request,
            ReconciliationAdapter reconAdapter,
            IConfigurationLoader configLoader,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest("Title is required.");

            Enum.TryParse<MediaType>(request.MediaType, ignoreCase: true, out var mediaType);
            var core = configLoader.LoadCore();

            var lookupRequest = new ProviderLookupRequest
            {
                EntityId      = Guid.NewGuid(),
                EntityType    = EntityType.MediaAsset,
                MediaType     = mediaType,
                Title         = request.Title,
                Author        = request.Author,
                Language      = core.Language.Metadata,
                Country       = core.Country  ?? "us",
                HydrationPass = HydrationPass.Universe,
                BaseUrl       = string.Empty,
            };

            IReadOnlyList<SearchResultItem> candidates;
            try
            {
                candidates = await reconAdapter.SearchAsync(lookupRequest, 25, ct);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    $"Reconciliation search failed: {ex.Message}",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var response = new DebugSearchResponse(
                candidates.Select(c =>
                {
                    var qid = c.ProviderItemId ?? string.Empty;
                    return new DebugSearchCandidate(
                        qid,
                        c.Title,
                        c.Description,
                        c.Confidence,
                        c.Confidence >= 0.95,
                        string.IsNullOrEmpty(qid) ? string.Empty : $"https://www.wikidata.org/wiki/{qid}");
                })
                .ToList());

            return Results.Ok(response);
        })
        .WithName("DebugSearch")
        .WithSummary("Step 1: Search Wikidata Reconciliation API for candidates. Returns a list for user selection.")
        .Produces<DebugSearchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status502BadGateway)
        .RequireAdmin();

        // ── POST /debug/enrich ──────────────────────────────────────────────────
        // Step 2: Takes a confirmed QID and runs full Data Extension + enrichment.
        group.MapPost("/enrich", async (
            DebugEnrichRequest request,
            ReconciliationAdapter reconAdapter,
            IEnumerable<IExternalMetadataProvider> providers,
            IPersonRepository personRepo,
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            IConfigurationLoader configLoader,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Qid))
                return Results.BadRequest("QID is required.");

            Enum.TryParse<MediaType>(request.MediaType, ignoreCase: true, out var mediaType);
            var core = configLoader.LoadCore();

            // Use PreResolvedQid to skip reconciliation and go straight to Data Extension.
            var lookupRequest = new ProviderLookupRequest
            {
                EntityId       = Guid.NewGuid(),
                EntityType     = EntityType.MediaAsset,
                MediaType      = mediaType,
                Title          = request.Qid, // Title not needed when QID is pre-resolved
                Author         = request.Author,
                Language       = core.Language.Metadata,
                Country        = core.Country  ?? "us",
                HydrationPass  = HydrationPass.Universe,
                BaseUrl        = string.Empty,
                PreResolvedQid = request.Qid,
            };

            IReadOnlyList<ProviderClaim> claims;
            try
            {
                claims = await reconAdapter.FetchAsync(lookupRequest, ct);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    $"Data Extension failed: {ex.Message}",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var providerId = reconAdapter.ProviderId.ToString();

            // Extract resolved QID (should match the input).
            var qidClaim = claims.FirstOrDefault(c =>
                string.Equals(c.Key, "wikidata_qid", StringComparison.OrdinalIgnoreCase));
            var resolvedQid = qidClaim?.Value ?? request.Qid;

            // Group claims by field key.
            var claimGroups = claims
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .Select(g => new DebugClaimGroup(
                    g.Key,
                    g.Select(c => new DebugClaimEntry(c.Value, c.Confidence, providerId))
                      .ToList()))
                .ToList();

            // Resolve persons from companion _qid claims.
            var personQids = ExtractCompanionQids(claims,
                "author_qid", "narrator_qid", "director_qid", "cast_qid",
                "screenwriter_qid", "composer_qid", "illustrator_qid");

            var persons = new List<DebugPersonResult>();
            foreach (var (qid, _) in personQids)
            {
                var person = await personRepo.FindByQidAsync(qid, ct);
                if (person is not null)
                {
                    persons.Add(new DebugPersonResult(
                        person.Name,
                        string.Join(", ", person.Roles),
                        person.WikidataQid,
                        person.HeadshotUrl,
                        person.Biography,
                        person.Occupation));
                }
            }

            // Resolve fictional entities from companion _qid claims.
            var entityQids = ExtractCompanionQids(claims,
                "character_qid", "location_qid", "organization_qid",
                "fictional_entity_qid", "cast_member_qid");

            var fictionalEntities = new List<DebugEntityResult>();
            var entityLabelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (qid, label) in entityQids)
            {
                entityLabelMap.TryAdd(qid, label);
                var entity = await entityRepo.FindByQidAsync(qid, ct);
                if (entity is not null)
                {
                    fictionalEntities.Add(new DebugEntityResult(
                        entity.Label,
                        entity.WikidataQid,
                        entity.EntitySubType,
                        entity.Description,
                        entity.ImageUrl));
                    entityLabelMap[qid] = entity.Label;
                }
            }

            // Resolve relationships for any entities found.
            var relationships = new List<DebugRelationshipResult>();
            var allEntityQidStrings = entityQids.Select(e => e.Qid).ToList();
            if (allEntityQidStrings.Count > 0)
            {
                var edges = await relRepo.GetByUniverseAsync(
                    allEntityQidStrings.ToHashSet(StringComparer.OrdinalIgnoreCase), ct);

                foreach (var edge in edges)
                {
                    entityLabelMap.TryGetValue(edge.SubjectQid, out var subjectLabel);
                    entityLabelMap.TryGetValue(edge.ObjectQid, out var objectLabel);

                    relationships.Add(new DebugRelationshipResult(
                        edge.SubjectQid,
                        subjectLabel ?? edge.SubjectQid,
                        edge.RelationshipTypeValue,
                        edge.ObjectQid,
                        objectLabel ?? edge.ObjectQid,
                        edge.StartTime,
                        edge.EndTime));
                }
            }

            var allProviderConfigs = configLoader.LoadAllProviders();
            var bridgeHintPreview = ComputeBridgeHintPreview(claims, mediaType.ToString(), allProviderConfigs);

            var response = new DebugLookupResponse(
                resolvedQid,
                claimGroups,
                persons,
                fictionalEntities,
                relationships,
                bridgeHintPreview);

            return Results.Ok(response);
        })
        .WithName("DebugEnrich")
        .WithSummary("Step 2: Takes a confirmed QID and runs full Data Extension + enrichment without persisting.")
        .Produces<DebugLookupResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status502BadGateway)
        .RequireAdmin();

        // ── POST /debug/enrich-universe ────────────────────────────────────────
        // Step 3: Takes a confirmed QID, runs Data Extension, and triggers
        // universe enrichment — creating Person and FictionalEntity records.
        group.MapPost("/enrich-universe", async (
            DebugEnrichRequest request,
            ReconciliationAdapter reconAdapter,
            IPersonRepository personRepo,
            IFictionalEntityRepository entityRepo,
            IEntityRelationshipRepository relRepo,
            IMetadataHarvestingService harvestService,
            IConfigurationLoader configLoader,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Qid))
                return Results.BadRequest("QID is required.");

            Enum.TryParse<MediaType>(request.MediaType, ignoreCase: true, out var mediaType);
            var core = configLoader.LoadCore();

            // Use PreResolvedQid to skip reconciliation and go straight to Data Extension.
            var lookupRequest = new ProviderLookupRequest
            {
                EntityId       = Guid.NewGuid(),
                EntityType     = EntityType.MediaAsset,
                MediaType      = mediaType,
                Title          = request.Qid,
                Author         = request.Author,
                Language       = core.Language.Metadata,
                Country        = core.Country  ?? "us",
                HydrationPass  = HydrationPass.Universe,
                BaseUrl        = string.Empty,
                PreResolvedQid = request.Qid,
            };

            IReadOnlyList<ProviderClaim> claims;
            try
            {
                claims = await reconAdapter.FetchAsync(lookupRequest, ct);
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    $"Data Extension failed: {ex.Message}",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var providerId = reconAdapter.ProviderId.ToString();

            // Extract resolved QID.
            var qidClaim = claims.FirstOrDefault(c =>
                string.Equals(c.Key, "wikidata_qid", StringComparison.OrdinalIgnoreCase));
            var resolvedQid = qidClaim?.Value ?? request.Qid;

            // Group claims by field key.
            var claimGroups = claims
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key)
                .Select(g => new DebugClaimGroup(
                    g.Key,
                    g.Select(c => new DebugClaimEntry(c.Value, c.Confidence, providerId))
                      .ToList()))
                .ToList();

            // ── Create/find Person records from companion _qid claims ────────
            var personQids = ExtractCompanionQids(claims,
                "author_qid", "narrator_qid", "director_qid", "cast_qid",
                "screenwriter_qid", "composer_qid", "illustrator_qid");

            var persons = new List<DebugPersonResult>();
            foreach (var (qid, label) in personQids)
            {
                var person = await personRepo.FindByQidAsync(qid, ct);
                if (person is null)
                {
                    // Determine role from the claim key.
                    var matchingClaim = claims.FirstOrDefault(c =>
                        c.Value.StartsWith(qid, StringComparison.OrdinalIgnoreCase) &&
                        c.Key.EndsWith("_qid", StringComparison.OrdinalIgnoreCase));
                    var role = matchingClaim?.Key switch
                    {
                        "author_qid"       => "Author",
                        "narrator_qid"     => "Narrator",
                        "director_qid"     => "Director",
                        "cast_qid"         => "Actor",
                        "screenwriter_qid" => "Screenwriter",
                        "composer_qid"     => "Composer",
                        _                  => "Author",
                    };

                    person = new Person
                    {
                        Id          = Guid.NewGuid(),
                        Name        = label,
                        Roles       = [role],
                        WikidataQid = qid,
                        CreatedAt   = DateTimeOffset.UtcNow,
                    };
                    person = await personRepo.CreateAsync(person, ct);

                    // Enqueue Wikidata enrichment for this person.
                    await harvestService.EnqueueAsync(new HarvestRequest
                    {
                        EntityId   = person.Id,
                        EntityType = EntityType.Person,
                        MediaType  = mediaType,
                        Hints      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = label,
                            ["qid"]  = qid,
                        },
                    }, ct);
                }

                persons.Add(new DebugPersonResult(
                    person.Name,
                    string.Join(", ", person.Roles),
                    person.WikidataQid,
                    person.HeadshotUrl,
                    person.Biography,
                    person.Occupation));
            }

            // ── Create/find FictionalEntity records from companion _qid claims ──
            var entityQids = ExtractCompanionQids(claims,
                "character_qid", "location_qid", "organization_qid",
                "fictional_entity_qid", "cast_member_qid");

            var fictionalEntities = new List<DebugEntityResult>();
            var entityLabelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Determine narrative root from claims for universe assignment.
            var universeQid = claims
                .Where(c => string.Equals(c.Key, "fictional_universe_qid", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(c.Key, "franchise_qid", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(c.Key, "series_qid", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Confidence)
                .Select(c =>
                {
                    var sep = c.Value.IndexOf("::", StringComparison.Ordinal);
                    return sep > 0 ? c.Value[..sep] : c.Value;
                })
                .FirstOrDefault();

            foreach (var (qid, label) in entityQids)
            {
                entityLabelMap.TryAdd(qid, label);
                var entity = await entityRepo.FindByQidAsync(qid, ct);
                if (entity is null)
                {
                    // Determine entity type from the claim key.
                    var matchingClaim = claims.FirstOrDefault(c =>
                        c.Value.StartsWith(qid, StringComparison.OrdinalIgnoreCase) &&
                        c.Key.EndsWith("_qid", StringComparison.OrdinalIgnoreCase));
                    var entitySubType = matchingClaim?.Key switch
                    {
                        "location_qid"     => "Location",
                        "organization_qid" => "Organization",
                        _                  => "Character",
                    };

                    entity = new FictionalEntity
                    {
                        Id                   = Guid.NewGuid(),
                        WikidataQid          = qid,
                        Label                = label,
                        EntitySubType        = entitySubType,
                        FictionalUniverseQid = universeQid,
                        CreatedAt            = DateTimeOffset.UtcNow,
                    };
                    await entityRepo.CreateAsync(entity, ct);

                    // Enqueue enrichment for this entity.
                    var entityType = entitySubType switch
                    {
                        "Location"     => EntityType.Location,
                        "Organization" => EntityType.Organization,
                        _              => EntityType.Character,
                    };
                    await harvestService.EnqueueAsync(new HarvestRequest
                    {
                        EntityId   = entity.Id,
                        EntityType = entityType,
                        MediaType  = mediaType,
                        Hints      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = label,
                            ["qid"]  = qid,
                        },
                    }, ct);
                }
                else
                {
                    entityLabelMap[qid] = entity.Label;
                }

                fictionalEntities.Add(new DebugEntityResult(
                    entity.Label,
                    entity.WikidataQid,
                    entity.EntitySubType,
                    entity.Description,
                    entity.ImageUrl));
            }

            // ── Resolve relationships for any entities found ──────────────────
            var relationships = new List<DebugRelationshipResult>();
            var allEntityQidStrings = entityQids.Select(e => e.Qid).ToList();
            if (allEntityQidStrings.Count > 0)
            {
                var edges = await relRepo.GetByUniverseAsync(
                    allEntityQidStrings.ToHashSet(StringComparer.OrdinalIgnoreCase), ct);

                foreach (var edge in edges)
                {
                    entityLabelMap.TryGetValue(edge.SubjectQid, out var subjectLabel);
                    entityLabelMap.TryGetValue(edge.ObjectQid, out var objectLabel);

                    relationships.Add(new DebugRelationshipResult(
                        edge.SubjectQid,
                        subjectLabel ?? edge.SubjectQid,
                        edge.RelationshipTypeValue,
                        edge.ObjectQid,
                        objectLabel ?? edge.ObjectQid,
                        edge.StartTime,
                        edge.EndTime));
                }
            }

            var allProviderConfigs = configLoader.LoadAllProviders();
            var bridgeHintPreview = ComputeBridgeHintPreview(claims, mediaType.ToString(), allProviderConfigs);

            var response = new DebugLookupResponse(
                resolvedQid,
                claimGroups,
                persons,
                fictionalEntities,
                relationships,
                bridgeHintPreview);

            return Results.Ok(response);
        })
        .WithName("DebugEnrichUniverse")
        .WithSummary("Step 3: Takes a confirmed QID, runs Data Extension, and creates Person + FictionalEntity records from companion QID claims.")
        .Produces<DebugLookupResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status502BadGateway)
        .RequireAdmin();

        return app;
    }

    /// <summary>
    /// Extracts QIDs from companion <c>_qid</c> claims.
    /// Companion claims use the value format <c>"Q12345::LabelText"</c>.
    /// Returns distinct (QID, Label) pairs across all matching field keys.
    /// </summary>
    private static IReadOnlyList<(string Qid, string Label)> ExtractCompanionQids(
        IReadOnlyList<ProviderClaim> claims,
        params string[] fieldKeys)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in claims)
        {
            if (!fieldKeys.Any(k => string.Equals(k, claim.Key, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Value format: "Q12345::LabelText" or just "Q12345".
            var separatorIdx = claim.Value.IndexOf("::", StringComparison.Ordinal);
            string qid, label;
            if (separatorIdx > 0)
            {
                qid   = claim.Value[..separatorIdx];
                label = claim.Value[(separatorIdx + 2)..];
            }
            else
            {
                qid   = claim.Value;
                label = claim.Value;
            }

            if (!string.IsNullOrWhiteSpace(qid))
                results.TryAdd(qid, label);
        }

        return results.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Simulates the hydration pipeline's ExtractBridgeHints logic using provider configs.
    /// Scans claims for bridge IDs that Stage 2 providers would use, applies normalization,
    /// and lists which providers would consume each hint.
    /// </summary>
    private static List<DebugBridgeHint> ComputeBridgeHintPreview(
        IReadOnlyList<ProviderClaim> claims,
        string mediaTypeName,
        IReadOnlyList<Storage.Models.ProviderConfiguration> allProviderConfigs)
    {
        var results = new List<DebugBridgeHint>();

        // Filter to Stage 2 providers only.
        var stage2Configs = allProviderConfigs
            .Where(c => c.HydrationStages.Contains(2))
            .ToList();

        if (stage2Configs.Count == 0) return results;

        // Collect desired bridge keys from all Stage 2 providers' preferred_bridge_ids.
        var desiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cfg in stage2Configs)
        {
            if (cfg.PreferredBridgeIds is null) continue;
            if (cfg.PreferredBridgeIds.TryGetValue(mediaTypeName, out var keys))
            {
                foreach (var k in keys) desiredKeys.Add(k);
            }
        }

        if (desiredKeys.Count == 0) return results;

        // Track which keys we've already emitted (first value wins, matching pipeline logic).
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in claims)
        {
            if (string.IsNullOrEmpty(claim.Value)) continue;

            // Check if the claim key matches directly or via alias.
            var effectiveKey = claim.Key;
            var alias = IdentifierNormalizationService.GetClaimKeyAlias(claim.Key);
            if (alias is not null) effectiveKey = alias;

            if (!desiredKeys.Contains(effectiveKey)) continue;
            if (emitted.Contains(effectiveKey)) continue;
            emitted.Add(effectiveKey);

            // Apply retail format normalization.
            var normalizedValue = effectiveKey switch
            {
                "isbn" => new string(claim.Value.Where(char.IsLetterOrDigit).ToArray()),
                "asin" => claim.Value.Trim().ToUpperInvariant(),
                _      => claim.Value.Trim()
            };

            if (string.IsNullOrWhiteSpace(normalizedValue)) continue;

            // Determine which providers would use this hint.
            var targetProviders = stage2Configs
                .Where(cfg =>
                {
                    if (cfg.PreferredBridgeIds is null) return false;
                    return cfg.PreferredBridgeIds.TryGetValue(mediaTypeName, out var pKeys)
                           && pKeys.Contains(effectiveKey, StringComparer.OrdinalIgnoreCase);
                })
                .Select(cfg => cfg.DisplayName ?? cfg.Name ?? "Unknown")
                .ToList();

            results.Add(new DebugBridgeHint(
                effectiveKey,
                claim.Value,
                normalizedValue,
                claim.Key,
                targetProviders));
        }

        return results;
    }
}
