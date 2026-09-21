using System.Net;
using System.Security.Cryptography;
using Dapper;
using MediaEngine.Contracts.Artwork;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Services;
using MediaEngine.Providers.Helpers;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// Canonical artwork boundary: one managed image and rendition set can be
/// linked to any number of entities and semantic roles.
/// </summary>
public sealed class ArtworkAssetService(
    IDatabaseConnection database,
    AssetPathService assetPaths,
    IHttpClientFactory httpClientFactory)
{
    private const int MaximumBytes = 10 * 1024 * 1024;

    public async Task<ArtworkAssetPageDto> BrowseAsync(
        string? search,
        string? role,
        string? aspect,
        Guid? targetEntityId,
        int offset,
        int limit,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var boundedOffset = Math.Max(0, offset);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        var normalizedRole = NormalizeOptionalRole(role);
        var normalizedAspect = string.IsNullOrWhiteSpace(aspect) ? null : aspect.Trim();

        using var connection = database.CreateConnection();
        const string where = """
            WHERE NULLIF(asset.original_path, '') IS NOT NULL
              AND (@search IS NULL OR EXISTS (
                SELECT 1 FROM artwork_asset_context context
                WHERE context.artwork_asset_id = asset.id
                  AND context.search_text LIKE @search COLLATE NOCASE))
              AND (@role IS NULL OR EXISTS (
                SELECT 1 FROM entity_artwork_links role_link
                WHERE role_link.artwork_asset_id = asset.id AND role_link.role = @role))
              AND (@aspect IS NULL OR asset.aspect_class = @aspect)
            """;

        var parameters = new
        {
            search = normalizedSearch,
            role = normalizedRole,
            aspect = normalizedAspect,
            targetEntityId,
            offset = boundedOffset,
            limit = boundedLimit,
        };
        var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM artwork_assets asset {where};", parameters);
        var assets = (await connection.QueryAsync<ArtworkAssetRow>($"""
            SELECT asset.id AS Id,
                   asset.width_px AS Width,
                   asset.height_px AS Height,
                   asset.aspect_class AS Aspect,
                   asset.source_provider AS SourceProvider,
                   asset.source_url AS SourceUrl,
                   CASE WHEN @targetEntityId IS NOT NULL AND EXISTS (
                       SELECT 1 FROM entity_artwork_links linked
                       WHERE linked.artwork_asset_id = asset.id AND linked.entity_id = @targetEntityId)
                   THEN 1 ELSE 0 END AS AlreadyLinked
            FROM artwork_assets asset
            {where}
            ORDER BY COALESCE(asset.updated_at, asset.created_at) DESC, asset.id
            LIMIT @limit OFFSET @offset;
            """, parameters)).ToList();

        var contexts = assets.Count == 0
            ? []
            : (await connection.QueryAsync<ArtworkContextRow>("""
                SELECT artwork_asset_id AS ArtworkAssetId,
                       entity_id AS EntityId,
                       entity_type AS EntityType,
                       entity_label AS EntityLabel,
                       media_type AS MediaType,
                       year AS Year,
                       role AS Role,
                       provider AS Provider
                FROM artwork_asset_context
                WHERE artwork_asset_id IN @assetIds
                ORDER BY entity_label, role;
                """, new { assetIds = assets.Select(asset => MediaEngine.Storage.GuidSql.ToBlob(asset.Id)).ToArray() })).ToList();
        var byAsset = contexts.ToLookup(context => context.ArtworkAssetId);
        var items = assets.Select(asset => ToDto(asset, byAsset[asset.Id])).ToList();
        return new ArtworkAssetPageDto(items, boundedOffset, boundedLimit, total);
    }

    public async Task<ArtworkEntityWorkspaceDto> GetEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var rows = (await connection.QueryAsync<ArtworkVariantRow>("""
            SELECT link.id AS LinkId,
                   link.artwork_asset_id AS ArtworkAssetId,
                   link.role AS Role,
                   NULLIF(link.context, '') AS Context,
                   link.source_asset_type AS SourceAssetType,
                   link.is_preferred AS IsPreferred,
                   link.is_user_override AS IsUserOverride,
                   asset.width_px AS Width,
                   asset.height_px AS Height,
                   asset.aspect_class AS Aspect,
                   asset.source_provider AS SourceProvider,
                   asset.source_url AS SourceUrl
            FROM entity_artwork_links link
            JOIN artwork_assets asset ON asset.id = link.artwork_asset_id
            WHERE link.entity_id = @entityId AND link.entity_type = @entityType
            ORDER BY link.role, link.is_preferred DESC, link.sort_order, link.created_at;
            """, new { entityId, entityType })).ToList();

        return new ArtworkEntityWorkspaceDto(entityId, entityType, rows.Select(ToVariantDto).ToList());
    }

    public async Task<ArtworkAssetDto> UploadAsync(
        Stream input,
        string extension,
        string entityType,
        Guid entityId,
        ArtworkLinkRequest request,
        string sourceProvider,
        string? sourceUrl,
        CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        if (buffer.Length == 0 || buffer.Length > MaximumBytes)
        {
            throw new InvalidOperationException("Artwork must be between 1 byte and 10 MB.");
        }

        var bytes = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var asset = await FindAssetByHashAsync(hash, ct);
        if (asset is null)
        {
            var assetId = Guid.NewGuid();
            var normalizedExtension = string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
            var originalPath = assetPaths.GetCentralAssetPath("ArtworkAsset", assetId, request.Role, assetId, normalizedExtension);
            AssetPathService.EnsureDirectory(originalPath);
            await File.WriteAllBytesAsync(originalPath, bytes, ct);

            var legacyShape = new EntityAsset
            {
                Id = assetId,
                EntityId = assetId.ToString("D"),
                EntityType = "ArtworkAsset",
                AssetTypeValue = LegacyAssetType(request.Role),
                LocalImagePath = originalPath,
                SourceProvider = sourceProvider,
                ImageUrl = sourceUrl,
                IsUserOverride = true,
                IsPreferred = request.Preferred,
            };
            ArtworkVariantHelper.StampMetadataAndRenditions(legacyShape, assetPaths);
            if (legacyShape.WidthPx is null || legacyShape.HeightPx is null)
            {
                File.Delete(originalPath);
                throw new InvalidOperationException("The uploaded file is not a supported image.");
            }
            await database.ExecuteWriteAsync((connection, transaction, token) =>
            {
                token.ThrowIfCancellationRequested();
                connection.Execute("""
                    INSERT INTO artwork_assets (
                        id, content_hash, original_path, small_path, medium_path, large_path,
                        width_px, height_px, aspect_class, primary_hex, secondary_hex, accent_hex,
                        source_provider, source_url, created_at)
                    VALUES (
                        @id, @hash, @originalPath, @smallPath, @mediumPath, @largePath,
                        @width, @height, @aspect, @primary, @secondary, @accent,
                        @provider, @sourceUrl, @createdAt);
                    INSERT OR IGNORE INTO image_cache(content_hash, file_path, source_url, downloaded_at, is_user_override)
                    VALUES (@hash, @originalPath, @sourceUrl, @createdAt, 1);
                    """, new
                    {
                        id = assetId,
                        hash,
                        originalPath,
                        smallPath = legacyShape.LocalImagePathSmall,
                        mediumPath = legacyShape.LocalImagePathMedium,
                        largePath = legacyShape.LocalImagePathLarge,
                        width = legacyShape.WidthPx,
                        height = legacyShape.HeightPx,
                        aspect = legacyShape.AspectClass,
                        primary = legacyShape.PrimaryHex,
                        secondary = legacyShape.SecondaryHex,
                        accent = legacyShape.AccentHex,
                        provider = sourceProvider,
                        sourceUrl,
                        createdAt = DateTimeOffset.UtcNow.ToString("O"),
                    }, transaction);
                return true;
            }, ct);
            asset = new ArtworkAssetRow
            {
                Id = assetId,
                Width = legacyShape.WidthPx,
                Height = legacyShape.HeightPx,
                Aspect = legacyShape.AspectClass,
                SourceProvider = sourceProvider,
                SourceUrl = sourceUrl,
            };
        }

        await LinkAsync(entityType, entityId, request with { ArtworkAssetId = asset.Id }, ct);
        return ToDto(asset, []);
    }

    public async Task<ArtworkAssetDto> AddFromUrlAsync(
        string entityType,
        Guid entityId,
        ArtworkFromUrlRequest request,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Artwork URL must use HTTP or HTTPS.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClientFactory.CreateClient("cover_download").SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw new InvalidOperationException($"Artwork URL returned {(int)response.StatusCode}.");
        }
        if (response.Content.Headers.ContentLength is > MaximumBytes)
        {
            throw new InvalidOperationException("Artwork must be 10 MB or smaller.");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var extension = string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var downloaded = await BoundedHttpContent.ReadImageAsync(response.Content, ct);
        await using var stream = new MemoryStream(downloaded, writable: false);
        return await UploadAsync(stream, extension, entityType, entityId,
            new ArtworkLinkRequest(Guid.Empty, request.Role, request.Context, request.Preferred,
                request.EntityLabel, request.MediaType, request.Year),
            "url", request.Url, ct);
    }

    public Task LinkAsync(string entityType, Guid entityId, ArtworkLinkRequest request, CancellationToken ct) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var role = NormalizeRole(request.Role);
            var context = request.Context?.Trim() ?? string.Empty;
            if (request.Preferred)
            {
                connection.Execute("""
                    UPDATE entity_artwork_links
                    SET is_preferred = 0, updated_at = @now
                    WHERE entity_id = @entityId AND entity_type = @entityType
                      AND role = @role AND context = @context;
                    """, new { entityId, entityType, role, context, now = DateTimeOffset.UtcNow.ToString("O") }, transaction);
            }

            var linkId = Guid.NewGuid();
            connection.Execute("""
                INSERT INTO entity_artwork_links (
                    id, entity_id, entity_type, artwork_asset_id, role, context,
                    is_preferred, is_user_override, created_at)
                VALUES (@linkId, @entityId, @entityType, @assetId, @role, @context,
                        @preferred, 1, @now)
                ON CONFLICT(entity_id, entity_type, artwork_asset_id, role, context) DO UPDATE SET
                    is_preferred = excluded.is_preferred,
                    is_user_override = 1,
                    updated_at = excluded.created_at;

                INSERT INTO artwork_asset_context (
                    artwork_asset_id, entity_id, entity_type, entity_label, media_type,
                    year, role, provider, search_text, created_at)
                SELECT @assetId, @entityId, @entityType, @label, @mediaType,
                       @year, @role, source_provider,
                       trim(@label || ' ' || @entityType || ' ' || COALESCE(@mediaType, '') || ' ' ||
                            COALESCE(@year, '') || ' ' || @role || ' ' || COALESCE(source_provider, '')),
                       @now
                FROM artwork_assets WHERE id = @assetId
                ON CONFLICT(artwork_asset_id, entity_id, entity_type, role) DO UPDATE SET
                    entity_label = excluded.entity_label,
                    media_type = excluded.media_type,
                    year = excluded.year,
                    provider = excluded.provider,
                    search_text = excluded.search_text,
                    updated_at = excluded.created_at;
                """, new
                {
                    linkId,
                    entityId,
                    entityType,
                    assetId = request.ArtworkAssetId,
                    role,
                    context,
                    preferred = request.Preferred ? 1 : 0,
                    label = string.IsNullOrWhiteSpace(request.EntityLabel) ? entityType : request.EntityLabel.Trim(),
                    request.MediaType,
                    request.Year,
                    now = DateTimeOffset.UtcNow.ToString("O"),
                }, transaction);

            var durableLinkId = connection.ExecuteScalar<Guid>("""
                SELECT id FROM entity_artwork_links
                WHERE entity_id=@entityId AND entity_type=@entityType AND artwork_asset_id=@assetId
                  AND role=@role AND context=@context
                LIMIT 1;
                """, new { entityId, entityType, assetId = request.ArtworkAssetId, role, context }, transaction);

            if (entityType is "Work" or "Person" or "Universe" or "FictionalEntity")
            {
                var legacyType = role switch
                {
                    "Background" => "Background",
                    "Portrait" => entityType == "FictionalEntity" ? "CharacterPortrait" : "Headshot",
                    "Logo" => "Logo",
                    _ => "CoverArt",
                };
                if (request.Preferred)
                {
                    connection.Execute("""
                        UPDATE entity_assets SET is_preferred=0, updated_at=@now
                        WHERE entity_id=@entityId AND entity_type=@entityType AND asset_type=@legacyType;
                        """, new { entityId, entityType, legacyType, now = DateTimeOffset.UtcNow.ToString("O") }, transaction);
                }
                connection.Execute("""
                    INSERT INTO entity_assets (
                        id, entity_id, entity_type, asset_type, image_url,
                        local_image_path, local_image_path_s, local_image_path_m, local_image_path_l,
                        source_provider, width_px, height_px, aspect_class,
                        primary_hex, secondary_hex, accent_hex, asset_class, storage_location, owner_scope,
                        is_preferred, is_user_override, created_at)
                    SELECT @linkId, @entityId, @entityType, @legacyType,
                           '/api/v1/display/artwork/assets/' || lower(hex(artwork.id)) || '/content',
                           artwork.original_path, artwork.small_path, artwork.medium_path, artwork.large_path,
                           artwork.source_provider, artwork.width_px, artwork.height_px, artwork.aspect_class,
                           artwork.primary_hex, artwork.secondary_hex, artwork.accent_hex,
                           'Artwork', 'Central', COALESCE(NULLIF(@context,''), @entityType),
                           @preferred, 1, @now
                    FROM artwork_assets artwork WHERE artwork.id=@assetId
                    ON CONFLICT(id) DO UPDATE SET
                        asset_type=excluded.asset_type,
                        local_image_path=excluded.local_image_path,
                        local_image_path_s=excluded.local_image_path_s,
                        local_image_path_m=excluded.local_image_path_m,
                        local_image_path_l=excluded.local_image_path_l,
                        is_preferred=excluded.is_preferred,
                        is_user_override=1,
                        updated_at=excluded.created_at;
                    """, new
                    {
                        linkId = durableLinkId,
                        entityId,
                        entityType,
                        legacyType,
                        assetId = request.ArtworkAssetId,
                        context,
                        preferred = request.Preferred ? 1 : 0,
                        now = DateTimeOffset.UtcNow.ToString("O"),
                    }, transaction);
            }

            if (request.Preferred && string.Equals(entityType, "Collection", StringComparison.OrdinalIgnoreCase))
            {
                var stored = connection.QuerySingleOrDefault<ArtworkStoredPathRow>("""
                    SELECT original_path AS OriginalPath FROM artwork_assets WHERE id = @assetId;
                    """, new { assetId = request.ArtworkAssetId }, transaction);
                if (!string.IsNullOrWhiteSpace(stored?.OriginalPath))
                {
                    var mimeType = Path.GetExtension(stored.OriginalPath).Equals(".png", StringComparison.OrdinalIgnoreCase)
                        ? "image/png" : "image/jpeg";
                    var update = role switch
                    {
                        "Background" => "UPDATE collections SET background_artwork_path=@path, background_artwork_mime_type=@mime WHERE id=@entityId;",
                        "Logo" => "UPDATE collections SET logo_artwork_path=@path, logo_artwork_mime_type=@mime WHERE id=@entityId;",
                        _ => "UPDATE collections SET cover_artwork_path=@path, cover_artwork_mime_type=@mime WHERE id=@entityId;",
                    };
                    connection.Execute(update, new { path = stored.OriginalPath, mime = mimeType, entityId }, transaction);
                }
            }
            return true;
        }, ct);

    public Task RemoveLinkAsync(Guid linkId, CancellationToken ct) =>
        database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var removed = connection.QuerySingleOrDefault<RemovedLinkRow>("""
                SELECT entity_id AS EntityId, entity_type AS EntityType, role AS Role,
                       is_preferred AS IsPreferred
                FROM entity_artwork_links WHERE id = @linkId;
                """, new { linkId }, transaction);
            connection.Execute("DELETE FROM entity_artwork_links WHERE id = @linkId;", new { linkId }, transaction);
            connection.Execute("DELETE FROM entity_assets WHERE id = @linkId AND is_user_override = 1;", new { linkId }, transaction);
            if (removed is { IsPreferred: true }
                && string.Equals(removed.EntityType, "Collection", StringComparison.OrdinalIgnoreCase))
            {
                var update = removed.Role switch
                {
                    "Background" => "UPDATE collections SET background_artwork_path=NULL, background_artwork_mime_type=NULL WHERE id=@entityId;",
                    "Logo" => "UPDATE collections SET logo_artwork_path=NULL, logo_artwork_mime_type=NULL WHERE id=@entityId;",
                    _ => "UPDATE collections SET cover_artwork_path=NULL, cover_artwork_mime_type=NULL WHERE id=@entityId;",
                };
                connection.Execute(update, new { entityId = removed.EntityId }, transaction);
            }
            return true;
        }, ct);

    public string? ResolveContentPath(Guid assetId, string? size)
    {
        using var connection = database.CreateConnection();
        var row = connection.QuerySingleOrDefault<ArtworkPathRow>("""
            SELECT original_path AS OriginalPath, small_path AS SmallPath,
                   medium_path AS MediumPath, large_path AS LargePath
            FROM artwork_assets WHERE id = @assetId;
            """, new { assetId });
        var selected = size?.ToLowerInvariant() switch
        {
            "s" => row?.SmallPath ?? row?.MediumPath ?? row?.OriginalPath,
            "m" => row?.MediumPath ?? row?.LargePath ?? row?.OriginalPath,
            "l" => row?.LargePath ?? row?.OriginalPath,
            _ => row?.OriginalPath ?? row?.LargePath ?? row?.MediumPath,
        };
        return !string.IsNullOrWhiteSpace(selected) && File.Exists(selected) ? selected : null;
    }

    private async Task<ArtworkAssetRow?> FindAssetByHashAsync(string hash, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ArtworkAssetRow>("""
            SELECT id AS Id, width_px AS Width, height_px AS Height,
                   aspect_class AS Aspect, source_provider AS SourceProvider, source_url AS SourceUrl
            FROM artwork_assets WHERE content_hash = @hash LIMIT 1;
            """, new { hash });
    }

    private static ArtworkAssetDto ToDto(ArtworkAssetRow asset, IEnumerable<ArtworkContextRow> contexts) =>
        new(asset.Id, $"/api/v1/display/artwork/assets/{asset.Id:D}/content?size=l",
            $"/api/v1/display/artwork/assets/{asset.Id:D}/content?size=s",
            asset.Width, asset.Height, asset.Aspect ?? "UnsupportedRect", asset.SourceProvider, asset.SourceUrl,
            contexts.Select(context => new ArtworkAssetContextDto(
                context.EntityId, context.EntityType, context.EntityLabel, context.MediaType,
                context.Year, context.Role, context.Provider)).ToList(), asset.AlreadyLinked);

    private static ArtworkEntityVariantDto ToVariantDto(ArtworkVariantRow row) =>
        new(row.LinkId, row.ArtworkAssetId, row.Role, row.Context, row.SourceAssetType,
            row.IsPreferred, row.IsUserOverride,
            $"/api/v1/display/artwork/assets/{row.ArtworkAssetId:D}/content?size=l",
            $"/api/v1/display/artwork/assets/{row.ArtworkAssetId:D}/content?size=s",
            row.Width, row.Height, row.Aspect ?? "UnsupportedRect", row.SourceProvider, row.SourceUrl);

    private static string NormalizeRole(string role) => role.Trim().ToLowerInvariant() switch
    {
        "primary" or "poster" or "coverart" or "seasonposter" or "episodestill" => "Primary",
        "background" or "banner" or "seasonthumb" => "Background",
        "portrait" or "headshot" or "characterportrait" => "Portrait",
        "logo" or "networklogo" or "studiologo" => "Logo",
        _ => throw new InvalidOperationException("Artwork role must be Primary, Background, Portrait, or Logo."),
    };

    private static string? NormalizeOptionalRole(string? role) =>
        string.IsNullOrWhiteSpace(role) ? null : NormalizeRole(role);

    private static string LegacyAssetType(string role) => NormalizeRole(role) switch
    {
        "Background" => "Background",
        "Portrait" => "Headshot",
        "Logo" => "Logo",
        _ => "CoverArt",
    };

    private sealed class ArtworkAssetRow
    {
        public Guid Id { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string? Aspect { get; init; }
        public string? SourceProvider { get; init; }
        public string? SourceUrl { get; init; }
        public bool AlreadyLinked { get; init; }
    }

    private sealed class ArtworkContextRow
    {
        public Guid ArtworkAssetId { get; init; }
        public Guid EntityId { get; init; }
        public string EntityType { get; init; } = string.Empty;
        public string EntityLabel { get; init; } = string.Empty;
        public string? MediaType { get; init; }
        public string? Year { get; init; }
        public string? Role { get; init; }
        public string? Provider { get; init; }
    }

    private sealed class ArtworkVariantRow
    {
        public Guid LinkId { get; init; }
        public Guid ArtworkAssetId { get; init; }
        public string Role { get; init; } = string.Empty;
        public string? Context { get; init; }
        public string? SourceAssetType { get; init; }
        public bool IsPreferred { get; init; }
        public bool IsUserOverride { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string? Aspect { get; init; }
        public string? SourceProvider { get; init; }
        public string? SourceUrl { get; init; }
    }

    private sealed class ArtworkPathRow
    {
        public string? OriginalPath { get; init; }
        public string? SmallPath { get; init; }
        public string? MediumPath { get; init; }
        public string? LargePath { get; init; }
    }

    private sealed class ArtworkStoredPathRow
    {
        public string? OriginalPath { get; init; }
    }

    private sealed class RemovedLinkRow
    {
        public Guid EntityId { get; init; }
        public string EntityType { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public bool IsPreferred { get; init; }
    }
}
