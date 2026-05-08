using Dapper;
using System.Text.Json;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class ItemCanonicalEndpoints
{
    public static IEndpointRouteBuilder MapItemCanonicalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/library/items")
            .WithTags("Items");

        group.MapPut("/{entityId:guid}/preferences", async (
            Guid entityId,
            ItemPreferencesRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            ISystemActivityRepository activityRepo,
            IWriteBackService writeBack,
            IEventPublisher publisher,
            IWorkRepository workRepo,
            IDatabaseConnection db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (request.Fields.Count == 0)
                return Results.BadRequest("At least one preference field is required.");

            var context = TryResolveWorkAssetContext(entityId, db);
            if (context is null)
                return Results.NotFound($"No media asset found for work {entityId}.");

            var now = DateTimeOffset.UtcNow;
            var claims = new List<MetadataClaim>();
            var canonicals = new List<CanonicalValue>();
            var updatedKeys = new List<string>();
            var lineage = await workRepo.GetLineageByAssetAsync(context.AssetId, ct);

            foreach (var (key, value) in request.Fields)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                var targetId = ResolveScopedTarget(context.AssetId, lineage, key);
                claims.Add(new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = targetId,
                    ProviderId = WellKnownProviders.UserManual,
                    ClaimKey = key,
                    ClaimValue = value,
                    ClaimedAt = now,
                    Confidence = 1.0,
                    IsUserLocked = true,
                });

                canonicals.Add(new CanonicalValue
                {
                    EntityId = targetId,
                    Key = key,
                    Value = value,
                    LastScoredAt = now,
                    IsConflicted = false,
                    NeedsReview = false,
                    WinningProviderId = WellKnownProviders.UserManual,
                });

                updatedKeys.Add(key);
            }

            if (updatedKeys.Count == 0)
                return Results.BadRequest("No valid preference fields were provided.");

            await claimRepo.InsertBatchAsync(claims, ct);
            await canonicalRepo.UpsertBatchAsync(canonicals, ct);

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = now,
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId = entityId,
                EntityType = "Work",
                CollectionName = context.WorkTitle,
                Detail = $"Saved item preferences for {updatedKeys.Count} field(s): {string.Join(", ", updatedKeys)}.",
            }, ct);

            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id = entityId,
                provider_name = "user_manual",
                updated_fields = updatedKeys.ToArray(),
            }, ct);

            try
            {
                await writeBack.WriteMetadataAsync(context.AssetId, "item_preferences", ct);
            }
            catch (Exception ex)
            {
                loggerFactory
                    .CreateLogger("MediaEngine.Api.Endpoints.ItemCanonicalEndpoints")
                    .LogWarning(ex, "Write-back failed after saving item preferences for {EntityId}.", entityId);
            }

            return Results.Ok(new ItemPreferencesResponse
            {
                EntityId = entityId,
                FieldsUpdated = updatedKeys.Count,
                UpdatedKeys = updatedKeys,
                Message = $"Saved {updatedKeys.Count} preference field(s).",
            });
        })
        .WithName("SaveItemPreferences")
        .WithSummary("Save user-preferred item fields without changing external IDs.")
        .Produces<ItemPreferencesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPut("/{entityId:guid}/display-overrides", async (
            Guid entityId,
            ItemDisplayOverridesRequest request,
            ISystemActivityRepository activityRepo,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            if (request.Fields.Count == 0)
                return Results.BadRequest("At least one display override is required.");

            using var conn = db.CreateConnection();
            var exists = conn.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM works WHERE id = @entityId;",
                new { entityId = entityId.ToString() }) > 0;

            if (!exists)
                return Results.NotFound($"No work found for {entityId}.");

            var current = LoadDisplayOverrides(conn, entityId);
            var updatedKeys = new List<string>();
            foreach (var (key, value) in request.Fields)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var normalizedKey = key.Trim();
                var normalizedValue = (value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalizedValue))
                {
                    if (current.Remove(normalizedKey))
                        updatedKeys.Add(normalizedKey);
                    continue;
                }

                current[normalizedKey] = normalizedValue;
                updatedKeys.Add(normalizedKey);
            }

            if (updatedKeys.Count == 0)
                return Results.BadRequest("No valid display override fields were provided.");

            conn.Execute(
                "UPDATE works SET display_overrides_json = @json WHERE id = @entityId;",
                new
                {
                    json = current.Count == 0 ? null : JsonSerializer.Serialize(current),
                    entityId = entityId.ToString(),
                });

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = DateTimeOffset.UtcNow,
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId = entityId,
                EntityType = "Work",
                Detail = $"Saved display overrides for {updatedKeys.Count} field(s): {string.Join(", ", updatedKeys.Distinct(StringComparer.OrdinalIgnoreCase))}.",
            }, ct);

            return Results.Ok(new ItemDisplayOverridesResponse
            {
                EntityId = entityId,
                FieldsUpdated = updatedKeys.Count,
                UpdatedKeys = updatedKeys.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                DisplayOverrides = current,
                Message = $"Saved {updatedKeys.Count} display override field(s).",
            });
        })
        .WithName("SaveItemDisplayOverrides")
        .WithSummary("Save presentation-only display overrides without changing canonical values.")
        .Produces<ItemDisplayOverridesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId:guid}/canonical-search", async (
            Guid entityId,
            ItemCanonicalSearchRequest request,
            ISearchService searchService,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var context = TryResolveWorkAssetContext(entityId, db);
            if (context is null)
                return Results.NotFound($"No media asset found for work {entityId}.");

            var mediaType = ResolveMediaType(request.MediaType, context.MediaType);
            var policy = ResolveTargetPolicy(mediaType, request.TargetKind, request.TargetFieldGroup);
            if (policy is null)
                return Results.BadRequest($"Unsupported target field group '{request.TargetFieldGroup}' for media type '{mediaType}'.");

            var draftFields = request.DraftFields
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var query = BuildCanonicalQuery(policy, draftFields, request.QueryOverride);
            if (string.IsNullOrWhiteSpace(query))
                return Results.BadRequest("A search query or draft field values are required.");

            var missingRequired = policy.RequiredFieldKeys
                .Where(key => !draftFields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                .ToList();

            var retailCandidates = new List<ItemCanonicalRetailCandidate>();
            var linkedCandidates = new List<ItemCanonicalLinkedCandidate>();

            if (policy.SearchRetail)
            {
                var retail = await searchService.SearchRetailAsync(
                    new Domain.Models.SearchRetailRequest(
                        Query: query,
                        MediaType: mediaType,
                        MaxCandidates: Math.Clamp(request.MaxCandidates, 1, 10),
                        LocalTitle: context.WorkTitle,
                        LocalAuthor: context.PrimaryCreator,
                        LocalYear: context.Year,
                        FileHints: draftFields.Count > 0 ? draftFields : null,
                        SearchFields: draftFields.Count > 0 ? draftFields : null),
                    ct);

                retailCandidates = retail.Candidates
                    .Select(candidate => BuildRetailCandidate(candidate, mediaType, policy))
                    .ToList();
            }

            if (policy.SearchUniverse)
            {
                var universe = await searchService.SearchUniverseAsync(
                    new Domain.Models.SearchUniverseRequest(
                        Query: query,
                        MediaType: mediaType,
                        MaxCandidates: Math.Clamp(request.MaxCandidates, 1, 10),
                        LocalTitle: context.WorkTitle,
                        LocalAuthor: context.PrimaryCreator,
                        LocalYear: context.Year),
                    ct);

                linkedCandidates = universe.Candidates
                    .Select(candidate => BuildLinkedCandidate(candidate, mediaType, policy))
                    .ToList();
            }

            var unlinkedFields = policy.RequiredFieldKeys
                .Concat(policy.SuggestedFieldKeys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(draftFields.ContainsKey)
                .ToDictionary(key => key, key => draftFields[key], StringComparer.OrdinalIgnoreCase);

            return Results.Ok(new ItemCanonicalSearchResponse
            {
                EntityId = entityId,
                MediaType = mediaType,
                TargetKind = policy.TargetKind,
                TargetFieldGroup = policy.TargetFieldGroup,
                Query = query,
                RetailCandidates = retailCandidates,
                LinkedCandidates = linkedCandidates,
                DraftFields = draftFields,
                FallbackActions =
                [
                    "Keep current canonical value",
                    "Save as preference only",
                    "Apply as unlinked canonical value",
                ],
                NoResultMessage = retailCandidates.Count == 0 && linkedCandidates.Count == 0
                    ? "No safe canonical result was found. Keep the current value, save the draft as a preference, or apply an unlinked canonical value when the required anchors are present."
                    : null,
                CanApplyUnlinkedCanonical = policy.AllowsTextOnly && missingRequired.Count == 0 && unlinkedFields.Count > 0,
                MissingRequiredFields = missingRequired,
                UnlinkedFields = unlinkedFields,
            });
        })
        .WithName("SearchItemCanonicalCandidates")
        .WithSummary("Run a targeted canonical search for a specific item field group.")
        .Produces<ItemCanonicalSearchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId:guid}/canonical-apply", async (
            Guid entityId,
            ItemCanonicalApplyRequest request,
            IMetadataClaimRepository claimRepo,
            ICanonicalValueRepository canonicalRepo,
            IBridgeIdRepository bridgeIdRepo,
            ISystemActivityRepository activityRepo,
            IWriteBackService writeBack,
            IEventPublisher publisher,
            ICollectionRepository collectionRepo,
            IWorkRepository workRepo,
            IDatabaseConnection db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var context = TryResolveWorkAssetContext(entityId, db);
            if (context is null)
                return Results.NotFound($"No media asset found for work {entityId}.");

            var policy = ResolveTargetPolicy(context.MediaType, request.TargetKind, request.TargetFieldGroup);
            if (policy is null)
                return Results.BadRequest($"Unsupported target field group '{request.TargetFieldGroup}' for media type '{context.MediaType}'.");

            var now = DateTimeOffset.UtcNow;
            var selectedSuggested = request.AcceptedSuggestedKeys
                .Where(key => request.SuggestedFields.ContainsKey(key))
                .ToDictionary(key => key, key => request.SuggestedFields[key], StringComparer.OrdinalIgnoreCase);
            var selectedFields = request.RequiredFields
                .Concat(selectedSuggested)
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            if (selectedFields.Count == 0 && request.BridgeIds.Count == 0 && request.QidFields.Count == 0)
                return Results.BadRequest("No canonical data was selected to apply.");

            var claims = new List<MetadataClaim>();
            var canonicals = new List<CanonicalValue>();
            var lineage = await workRepo.GetLineageByAssetAsync(context.AssetId, ct);

            foreach (var (key, value) in selectedFields.Concat(request.QidFields))
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var targetId = ResolveScopedTarget(context.AssetId, lineage, key);
                claims.Add(new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = targetId,
                    ProviderId = WellKnownProviders.UserManual,
                    ClaimKey = key,
                    ClaimValue = value,
                    ClaimedAt = now,
                    Confidence = 1.0,
                    IsUserLocked = true,
                });

                canonicals.Add(new CanonicalValue
                {
                    EntityId = targetId,
                    Key = key,
                    Value = value,
                    LastScoredAt = now,
                    IsConflicted = false,
                    NeedsReview = false,
                    WinningProviderId = WellKnownProviders.UserManual,
                });
            }

            if (claims.Count > 0)
                await claimRepo.InsertBatchAsync(claims, ct);
            if (canonicals.Count > 0)
                await canonicalRepo.UpsertBatchAsync(canonicals, ct);

            var clearedIds = await ClearStaleIdsAsync(context.AssetId, lineage, policy, request, canonicalRepo, db, ct);

            if (request.BridgeIds.Count > 0)
            {
                var bridgeEntries = request.BridgeIds
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                    .Select(kv => new BridgeIdEntry
                    {
                        EntityId = ResolveScopedTarget(context.AssetId, lineage, kv.Key),
                        IdType = kv.Key,
                        IdValue = kv.Value,
                        ProviderId = WellKnownProviders.UserManual.ToString(),
                        CreatedAt = now,
                    })
                    .ToList();

                await bridgeIdRepo.UpsertBatchAsync(bridgeEntries, ct);

                var bridgeClaims = bridgeEntries.Select(entry => new MetadataClaim
                {
                    Id = Guid.NewGuid(),
                    EntityId = entry.EntityId,
                    ProviderId = WellKnownProviders.UserManual,
                    ClaimKey = entry.IdType,
                    ClaimValue = entry.IdValue,
                    ClaimedAt = now,
                    Confidence = 1.0,
                    IsUserLocked = true,
                }).ToList();
                var bridgeCanonicals = bridgeEntries.Select(entry => new CanonicalValue
                {
                    EntityId = entry.EntityId,
                    Key = entry.IdType,
                    Value = entry.IdValue,
                    LastScoredAt = now,
                    IsConflicted = false,
                    NeedsReview = false,
                    WinningProviderId = WellKnownProviders.UserManual,
                }).ToList();

                await claimRepo.InsertBatchAsync(bridgeClaims, ct);
                await canonicalRepo.UpsertBatchAsync(bridgeCanonicals, ct);
            }

            if (request.QidFields.TryGetValue(BridgeIdKeys.WikidataQid, out var globalQid) && !string.IsNullOrWhiteSpace(globalQid))
            {
                using var conn = db.CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    UPDATE works
                    SET wikidata_qid = @qid, curator_state = 'registered', rejected_at = NULL
                    WHERE id = @workId
                    """;
                cmd.Parameters.AddWithValue("@qid", globalQid);
                var qidTargetId = ResolveScopedTarget(context.AssetId, lineage, BridgeIdKeys.WikidataQid);
                cmd.Parameters.AddWithValue("@workId", qidTargetId.ToString());
                cmd.ExecuteNonQuery();
                await collectionRepo.UpdateWorkWikidataStatusAsync(qidTargetId, "confirmed", ct);
            }
            else if (string.Equals(NormalizeLinkState(request.LinkState), "provider_only", StringComparison.Ordinal))
            {
                await collectionRepo.UpdateWorkWikidataStatusAsync(
                    ResolveScopedTarget(context.AssetId, lineage, BridgeIdKeys.WikidataQid),
                    "missing",
                    ct);
            }

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = now,
                ActionType = SystemActionType.MetadataManualOverride,
                EntityId = entityId,
                EntityType = "Work",
                CollectionName = context.WorkTitle,
                Detail = $"Applied canonical {policy.TargetFieldGroup} selection ({NormalizeLinkState(request.LinkState)}) with {selectedFields.Count} field(s).",
            }, ct);

            await publisher.PublishAsync(SignalREvents.MetadataHarvested, new
            {
                entity_id = entityId,
                provider_name = "user_manual",
                updated_fields = selectedFields.Keys
                    .Concat(request.QidFields.Keys)
                    .Concat(request.BridgeIds.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            }, ct);

            try
            {
                await writeBack.WriteMetadataAsync(context.AssetId, "item_canonical_apply", ct);
            }
            catch (Exception ex)
            {
                loggerFactory
                    .CreateLogger("MediaEngine.Api.Endpoints.ItemCanonicalEndpoints")
                    .LogWarning(ex, "Write-back failed after applying canonical candidate for {EntityId}.", entityId);
            }

            return Results.Ok(new ItemCanonicalApplyResponse
            {
                EntityId = entityId,
                LinkState = NormalizeLinkState(request.LinkState),
                LinkStatusLabel = GetLinkStatusLabel(request.LinkState),
                FieldsApplied = selectedFields.Count,
                IdsCleared = clearedIds,
                Message = $"Applied canonical {policy.TargetFieldGroup} fields.",
            });
        })
        .WithName("ApplyItemCanonicalCandidate")
        .WithSummary("Apply a targeted canonical candidate and clear stale IDs for the same field group.")
        .Produces<ItemCanonicalApplyResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        return app;
    }

    private sealed record WorkAssetContext(
        Guid AssetId,
        string AssetIdText,
        string MediaType,
        string? WorkTitle,
        string? PrimaryCreator,
        string? Year);

    private sealed record CanonicalTargetPolicy(
        string MediaType,
        string TargetKind,
        string TargetFieldGroup,
        IReadOnlyList<string> RequiredFieldKeys,
        IReadOnlyList<string> SuggestedFieldKeys,
        IReadOnlyList<string> BridgeIdKeys,
        IReadOnlyList<string> QidFieldKeys,
        IReadOnlyList<string> QueryFieldKeys,
        bool SearchRetail,
        bool SearchUniverse,
        bool AllowsTextOnly);

    private static WorkAssetContext? TryResolveWorkAssetContext(Guid workId, IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(ma.id, child_ma.id, grandchild_ma.id),
                COALESCE(NULLIF(w.media_type, ''), MAX(CASE WHEN cv.key = 'media_type' THEN cv.value END), ''),
                COALESCE(MAX(CASE WHEN cv.key = 'title' THEN cv.value END), ''),
                COALESCE(MAX(CASE WHEN cv.key IN ('author', 'artist', 'director') THEN cv.value END), ''),
                COALESCE(MAX(CASE WHEN cv.key = 'year' THEN cv.value END), '')
            FROM works w
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            LEFT JOIN works child ON child.parent_work_id = w.id
            LEFT JOIN editions child_e ON child_e.work_id = child.id
            LEFT JOIN media_assets child_ma ON child_ma.edition_id = child_e.id
            LEFT JOIN works grandchild ON grandchild.parent_work_id = child.id
            LEFT JOIN editions grandchild_e ON grandchild_e.work_id = grandchild.id
            LEFT JOIN media_assets grandchild_ma ON grandchild_ma.edition_id = grandchild_e.id
            LEFT JOIN canonical_values cv ON cv.entity_id = COALESCE(ma.id, child_ma.id, grandchild_ma.id)
            WHERE w.id = @workId OR ma.id = @workId
            GROUP BY COALESCE(ma.id, child_ma.id, grandchild_ma.id), w.media_type
            ORDER BY COALESCE(ma.id, child_ma.id, grandchild_ma.id)
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@workId", workId.ToString());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var assetIdText = reader.GetString(0);
        if (!Guid.TryParse(assetIdText, out var assetId))
            return null;

        return new WorkAssetContext(
            AssetId: assetId,
            AssetIdText: assetIdText,
            MediaType: reader.IsDBNull(1) ? MediaType.Unknown.ToString() : reader.GetString(1),
            WorkTitle: reader.IsDBNull(2) ? null : reader.GetString(2),
            PrimaryCreator: reader.IsDBNull(3) ? null : reader.GetString(3),
            Year: reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static string ResolveMediaType(string? requestedMediaType, string fallbackMediaType) =>
        !string.IsNullOrWhiteSpace(requestedMediaType)
            ? requestedMediaType
            : (string.IsNullOrWhiteSpace(fallbackMediaType) ? MediaType.Unknown.ToString() : fallbackMediaType);

    private static CanonicalTargetPolicy? ResolveTargetPolicy(string mediaType, string targetKind, string targetFieldGroup) =>
        (mediaType.Trim(), targetFieldGroup.Trim().ToLowerInvariant()) switch
        {
            ("Music", "album") => new CanonicalTargetPolicy(mediaType, "container", "album",
                [MetadataFieldConstants.Artist, MetadataFieldConstants.Album],
                [MetadataFieldConstants.Title, MetadataFieldConstants.TrackNumber, MetadataFieldConstants.Year, MetadataFieldConstants.Composer, MetadataFieldConstants.Genre, MetadataFieldConstants.DurationField, "disc_number"],
                [BridgeIdKeys.AppleArtistId, BridgeIdKeys.AppleMusicCollectionId, BridgeIdKeys.AppleMusicId, BridgeIdKeys.MusicBrainzId, BridgeIdKeys.MusicBrainzReleaseGroupId, BridgeIdKeys.MusicBrainzRecordingId],
                ["album_qid"],
                [MetadataFieldConstants.Artist, MetadataFieldConstants.Album, MetadataFieldConstants.Title],
                true, true, true),
            ("Music", "artist") => new CanonicalTargetPolicy(mediaType, "person", "artist",
                [MetadataFieldConstants.Artist],
                [MetadataFieldConstants.Genre],
                [BridgeIdKeys.AppleArtistId, BridgeIdKeys.MusicBrainzId],
                ["artist_qid"],
                [MetadataFieldConstants.Artist],
                true, true, true),
            ("Audiobooks", "narrator") => new CanonicalTargetPolicy(mediaType, "person", "narrator",
                [MetadataFieldConstants.Narrator],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition],
                [],
                ["narrator_qid"],
                [MetadataFieldConstants.Narrator, MetadataFieldConstants.Title, MetadataFieldConstants.Author],
                false, true, true),
            ("Audiobooks", "series") => new CanonicalTargetPolicy(mediaType, "container", "series",
                [MetadataFieldConstants.Series],
                [MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Year],
                [BridgeIdKeys.AudibleId, BridgeIdKeys.Isbn, BridgeIdKeys.Asin],
                ["series_qid"],
                [MetadataFieldConstants.Series, MetadataFieldConstants.Author],
                true, true, true),
            ("Audiobooks", "audiobook_identity") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "audiobook_identity",
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Narrator],
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Year, MetadataFieldConstants.PublisherField, MetadataFieldConstants.DurationField, MetadataFieldConstants.Genre],
                [BridgeIdKeys.AudibleId, BridgeIdKeys.Isbn, BridgeIdKeys.Asin],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Narrator],
                true, true, true),
            ("Books", "series") => new CanonicalTargetPolicy(mediaType, "container", "series",
                [MetadataFieldConstants.Series],
                [MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Year],
                [BridgeIdKeys.Isbn, BridgeIdKeys.Asin],
                ["series_qid"],
                [MetadataFieldConstants.Series, MetadataFieldConstants.Author],
                true, true, true),
            ("Books", "book_identity") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "book_identity",
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author],
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Year, MetadataFieldConstants.PublisherField, MetadataFieldConstants.Genre, MetadataFieldConstants.Language],
                [BridgeIdKeys.Isbn, BridgeIdKeys.Asin, BridgeIdKeys.AppleBooksId, BridgeIdKeys.OpenLibraryId],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Author, MetadataFieldConstants.Series],
                true, true, true),
            ("Movies", "movie_identity") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "movie_identity",
                [MetadataFieldConstants.Title, MetadataFieldConstants.Year],
                [MetadataFieldConstants.Director, MetadataFieldConstants.OriginalTitle, MetadataFieldConstants.Runtime, MetadataFieldConstants.Genre, MetadataFieldConstants.Composer, MetadataFieldConstants.CastMember],
                [BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Year, MetadataFieldConstants.Director],
                true, true, true),
            ("TV", "show") => new CanonicalTargetPolicy(mediaType, "container", "show",
                [MetadataFieldConstants.ShowName],
                [MetadataFieldConstants.Year, MetadataFieldConstants.Network],
                [BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId],
                ["series_qid"],
                [MetadataFieldConstants.ShowName, MetadataFieldConstants.Year],
                true, true, true),
            ("TV", "show_episode") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "show_episode",
                [MetadataFieldConstants.ShowName, MetadataFieldConstants.SeasonNumber, MetadataFieldConstants.EpisodeNumber],
                [MetadataFieldConstants.EpisodeTitle, MetadataFieldConstants.Year, MetadataFieldConstants.Runtime, MetadataFieldConstants.Director, MetadataFieldConstants.CastMember],
                [BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.ShowName, MetadataFieldConstants.SeasonNumber, MetadataFieldConstants.EpisodeNumber, MetadataFieldConstants.EpisodeTitle],
                true, true, true),
            ("Comics", "series") => new CanonicalTargetPolicy(mediaType, "container", "series",
                [MetadataFieldConstants.Series],
                [MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title, MetadataFieldConstants.Year, MetadataFieldConstants.Author, MetadataFieldConstants.Illustrator, MetadataFieldConstants.PublisherField, MetadataFieldConstants.Genre],
                [BridgeIdKeys.ComicVineId, BridgeIdKeys.Isbn],
                ["series_qid"],
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title],
                true, true, true),
            ("Comics", "issue") => new CanonicalTargetPolicy(mediaType, string.IsNullOrWhiteSpace(targetKind) ? "item" : targetKind, "issue",
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition],
                [MetadataFieldConstants.Title, MetadataFieldConstants.Year, MetadataFieldConstants.Author, MetadataFieldConstants.Illustrator, MetadataFieldConstants.PublisherField, MetadataFieldConstants.Genre],
                [BridgeIdKeys.ComicVineId, BridgeIdKeys.Isbn],
                [BridgeIdKeys.WikidataQid],
                [MetadataFieldConstants.Series, MetadataFieldConstants.SeriesPosition, MetadataFieldConstants.Title],
                true, true, true),
            _ => null,
        };

    private static string BuildCanonicalQuery(CanonicalTargetPolicy policy, IReadOnlyDictionary<string, string> draftFields, string? queryOverride)
    {
        if (!string.IsNullOrWhiteSpace(queryOverride))
            return queryOverride.Trim();

        return string.Join(" ", policy.QueryFieldKeys
            .Where(draftFields.ContainsKey)
            .Select(key => draftFields[key].Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static ItemCanonicalRetailCandidate BuildRetailCandidate(
        Domain.Models.RetailCandidate candidate,
        string mediaType,
        CanonicalTargetPolicy policy)
    {
        var allFields = BuildRetailFieldBag(candidate, mediaType, policy.TargetFieldGroup);
        var requiredFields = ExtractFields(allFields, policy.RequiredFieldKeys);
        var suggestedFields = ExtractFields(allFields, policy.SuggestedFieldKeys);
        var bridgeIds = ExtractFields(allFields, policy.BridgeIdKeys);
        var missingRequired = policy.RequiredFieldKeys.Where(key => !requiredFields.ContainsKey(key)).ToList();

        return new ItemCanonicalRetailCandidate
        {
            CandidateId = $"{candidate.ProviderName}:{candidate.ProviderItemId ?? candidate.Title}",
            ProviderId = candidate.ProviderId,
            ProviderName = candidate.ProviderName,
            ProviderItemId = candidate.ProviderItemId,
            Title = candidate.Title,
            Year = candidate.Year,
            Author = candidate.Author,
            Director = candidate.Director,
            Description = candidate.Description,
            CoverUrl = candidate.CoverUrl,
            Confidence = candidate.Confidence,
            CompositeScore = candidate.CompositeScore,
            ExtraFields = candidate.ExtraFields?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            LinkState = "provider_only",
            LinkStatusLabel = "Linked to provider only",
            IsApplicable = missingRequired.Count == 0,
            BlockedReason = missingRequired.Count == 0 ? null : $"Missing required anchors: {string.Join(", ", missingRequired)}.",
            RequiredFields = requiredFields,
            SuggestedFields = suggestedFields,
            BridgeIds = bridgeIds,
            QidFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
    }

    private static ItemCanonicalLinkedCandidate BuildLinkedCandidate(
        Domain.Models.UniverseCandidate candidate,
        string mediaType,
        CanonicalTargetPolicy policy)
    {
        var allFields = BuildUniverseFieldBag(candidate, mediaType, policy.TargetFieldGroup);
        var requiredFields = ExtractFields(allFields, policy.RequiredFieldKeys);
        var suggestedFields = ExtractFields(allFields, policy.SuggestedFieldKeys);
        var qidFields = policy.QidFieldKeys.ToDictionary(key => key, _ => candidate.Qid, StringComparer.OrdinalIgnoreCase);
        var missingRequired = policy.RequiredFieldKeys.Where(key => !requiredFields.ContainsKey(key)).ToList();

        return new ItemCanonicalLinkedCandidate
        {
            CandidateId = $"wikidata:{candidate.Qid}",
            Qid = candidate.Qid,
            Label = candidate.Label,
            Description = candidate.Description,
            InstanceOf = candidate.InstanceOf,
            Year = candidate.Year,
            Author = candidate.Author,
            Director = candidate.Director,
            CoverUrl = candidate.CoverUrl,
            WikipediaExtract = candidate.WikipediaExtract,
            ResolutionTier = candidate.ResolutionTier,
            Confidence = candidate.Confidence,
            BridgeIds = candidate.BridgeIds?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MediaTypeMetadata = candidate.MediaTypeMetadata?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            LinkState = "linked",
            LinkStatusLabel = "Linked to Wikidata",
            IsApplicable = missingRequired.Count == 0,
            BlockedReason = missingRequired.Count == 0 ? null : $"Missing required anchors: {string.Join(", ", missingRequired)}.",
            RequiredFields = requiredFields,
            SuggestedFields = suggestedFields,
            QidFields = qidFields,
        };
    }

    private static Dictionary<string, string> BuildRetailFieldBag(Domain.Models.RetailCandidate candidate, string mediaType, string targetFieldGroup)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(candidate.Title))
            fields[MetadataFieldConstants.Title] = candidate.Title;
        if (!string.IsNullOrWhiteSpace(candidate.Author))
        {
            fields[MetadataFieldConstants.Author] = candidate.Author;
            fields.TryAdd(MetadataFieldConstants.Artist, candidate.Author);
        }
        if (!string.IsNullOrWhiteSpace(candidate.Director))
            fields[MetadataFieldConstants.Director] = candidate.Director;
        if (!string.IsNullOrWhiteSpace(candidate.Description))
            fields[MetadataFieldConstants.Description] = candidate.Description;
        if (!string.IsNullOrWhiteSpace(candidate.Year))
            fields[MetadataFieldConstants.Year] = candidate.Year;
        if (!string.IsNullOrWhiteSpace(candidate.CoverUrl))
            fields[MetadataFieldConstants.CoverUrl] = candidate.CoverUrl;
        if (!string.IsNullOrWhiteSpace(candidate.ProviderItemId))
            fields["provider_item_id"] = candidate.ProviderItemId;

        foreach (var (key, value) in candidate.ExtraFields ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields[key] = value;
        }

        if (!string.IsNullOrWhiteSpace(candidate.ProviderItemId))
        {
            var guessedBridgeId = GuessBridgeIdKey(candidate.ProviderName, mediaType, targetFieldGroup);
            if (!string.IsNullOrWhiteSpace(guessedBridgeId))
                fields.TryAdd(guessedBridgeId, candidate.ProviderItemId!);
        }

        return fields;
    }

    private static Dictionary<string, string> BuildUniverseFieldBag(Domain.Models.UniverseCandidate candidate, string mediaType, string targetFieldGroup)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(candidate.Label))
            fields[MetadataFieldConstants.Title] = candidate.Label;
        if (!string.IsNullOrWhiteSpace(candidate.Author))
        {
            fields[MetadataFieldConstants.Author] = candidate.Author;
            fields.TryAdd(MetadataFieldConstants.Artist, candidate.Author);
        }
        if (!string.IsNullOrWhiteSpace(candidate.Director))
            fields[MetadataFieldConstants.Director] = candidate.Director;
        if (!string.IsNullOrWhiteSpace(candidate.Description))
            fields[MetadataFieldConstants.Description] = candidate.Description;
        if (!string.IsNullOrWhiteSpace(candidate.Year))
            fields[MetadataFieldConstants.Year] = candidate.Year;
        if (!string.IsNullOrWhiteSpace(candidate.CoverUrl))
            fields[MetadataFieldConstants.CoverUrl] = candidate.CoverUrl;

        foreach (var (key, value) in candidate.MediaTypeMetadata ?? new Dictionary<string, string>())
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields[key] = value;
        }

        switch (targetFieldGroup)
        {
            case "album":
                fields[MetadataFieldConstants.Album] = candidate.Label;
                break;
            case "artist":
                fields[MetadataFieldConstants.Artist] = candidate.Label;
                break;
            case "narrator":
                fields[MetadataFieldConstants.Narrator] = candidate.Label;
                break;
            case "series":
                fields[MetadataFieldConstants.Series] = candidate.Label;
                break;
            case "show":
                fields[MetadataFieldConstants.ShowName] = candidate.Label;
                break;
            case "show_episode":
                fields.TryAdd(MetadataFieldConstants.EpisodeTitle, candidate.Label);
                break;
            case "movie_identity":
            case "book_identity":
            case "audiobook_identity":
            case "issue":
                fields[MetadataFieldConstants.Title] = candidate.Label;
                break;
        }

        if (string.Equals(mediaType, MediaType.Music.ToString(), StringComparison.OrdinalIgnoreCase)
            && fields.TryGetValue(MetadataFieldConstants.Author, out var creator))
        {
            fields.TryAdd(MetadataFieldConstants.Artist, creator);
        }

        return fields;
    }

    private static Dictionary<string, string> ExtractFields(IReadOnlyDictionary<string, string> source, IEnumerable<string> keys)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (TryResolveFieldValue(source, key, out var value))
                output[key] = value;
        }

        return output;
    }

    private static bool TryResolveFieldValue(IReadOnlyDictionary<string, string> source, string key, out string value)
    {
        if (source.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value))
            return true;

        var aliases = key switch
        {
            MetadataFieldConstants.Artist => [MetadataFieldConstants.Author],
            MetadataFieldConstants.Author => [MetadataFieldConstants.Artist],
            MetadataFieldConstants.Album => [MetadataFieldConstants.Title],
            MetadataFieldConstants.Series => [MetadataFieldConstants.Title],
            MetadataFieldConstants.ShowName => [MetadataFieldConstants.Title],
            _ => Array.Empty<string>(),
        };

        foreach (var alias in aliases)
        {
            if (source.TryGetValue(alias, out value!) && !string.IsNullOrWhiteSpace(value))
                return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? GuessBridgeIdKey(string providerName, string mediaType, string targetFieldGroup)
    {
        var normalized = providerName?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Contains("comic"))
            return BridgeIdKeys.ComicVineId;
        if (normalized.Contains("tmdb"))
            return BridgeIdKeys.TmdbId;
        if (normalized.Contains("imdb"))
            return BridgeIdKeys.ImdbId;
        if (normalized.Contains("audible"))
            return BridgeIdKeys.AudibleId;
        if (normalized.Contains("apple_books"))
            return BridgeIdKeys.AppleBooksId;
        if (normalized.Contains("open_library"))
            return BridgeIdKeys.OpenLibraryId;
        if (normalized.Contains("apple_music"))
        {
            return targetFieldGroup switch
            {
                "artist" => BridgeIdKeys.AppleArtistId,
                "album" => BridgeIdKeys.AppleMusicCollectionId,
                _ => BridgeIdKeys.AppleMusicId,
            };
        }

        return mediaType switch
        {
            "Music" when targetFieldGroup == "album" => BridgeIdKeys.AppleMusicCollectionId,
            "Music" when targetFieldGroup == "artist" => BridgeIdKeys.AppleArtistId,
            _ => null,
        };
    }

    private static async Task<IReadOnlyList<string>> ClearStaleIdsAsync(
        Guid assetId,
        WorkLineage? lineage,
        CanonicalTargetPolicy policy,
        ItemCanonicalApplyRequest request,
        ICanonicalValueRepository canonicalRepo,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var retainedIdKeys = request.BridgeIds.Keys
            .Concat(request.QidFields.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groupIdKeys = policy.BridgeIdKeys.Concat(policy.QidFieldKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var toClear = groupIdKeys.Where(key => !retainedIdKeys.Contains(key)).ToList();

        foreach (var key in toClear)
            await canonicalRepo.DeleteByKeyAsync(ResolveScopedTarget(assetId, lineage, key), key, ct);

        if (toClear.Count > 0)
        {
            using var conn = db.CreateConnection();
            foreach (var key in toClear)
            {
                var targetId = ResolveScopedTarget(assetId, lineage, key);
                using var claimCmd = conn.CreateCommand();
                claimCmd.CommandText = "DELETE FROM metadata_claims WHERE entity_id = @entityId AND claim_key = @key";
                claimCmd.Parameters.AddWithValue("@entityId", targetId.ToString());
                claimCmd.Parameters.AddWithValue("@key", key);
                claimCmd.ExecuteNonQuery();

                using var bridgeCmd = conn.CreateCommand();
                bridgeCmd.CommandText = "DELETE FROM bridge_ids WHERE entity_id = @entityId AND id_type = @key";
                bridgeCmd.Parameters.AddWithValue("@entityId", targetId.ToString());
                bridgeCmd.Parameters.AddWithValue("@key", key);
                bridgeCmd.ExecuteNonQuery();
            }
        }

        return toClear;
    }

    private static Guid ResolveScopedTarget(Guid assetId, WorkLineage? lineage, string key)
    {
        if (lineage is null)
            return assetId;

        return ClaimScopeCatalog.IsParentScoped(key, lineage.MediaType)
            ? lineage.TargetForParentScope
            : assetId;
    }

    private static string NormalizeLinkState(string linkState) => linkState.Trim().ToLowerInvariant() switch
    {
        "linked" => "linked",
        "provider_only" => "provider_only",
        _ => "text_only",
    };

    private static string GetLinkStatusLabel(string linkState) => NormalizeLinkState(linkState) switch
    {
        "linked" => "Linked to Wikidata",
        "provider_only" => "Linked to provider only",
        _ => "Saved without external link",
    };

    private static Dictionary<string, string> LoadDisplayOverrides(System.Data.IDbConnection conn, Guid entityId)
    {
        var json = conn.QueryFirstOrDefault<string?>(
            "SELECT display_overrides_json FROM works WHERE id = @entityId LIMIT 1;",
            new { entityId = entityId.ToString() });

        if (string.IsNullOrWhiteSpace(json))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
