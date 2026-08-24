using System.Text.Json;
using MediaEngine.Api.Models;
using MediaEngine.Contracts.Collections;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Api.Services.Collections;

public sealed record CollectionPersonalMediaListResult(
    bool Found,
    bool Allowed,
    IReadOnlyList<CollectionPersonalMediaSourceDto> Sources);

public sealed record CollectionPersonalMediaWriteResult(
    bool Found,
    bool Allowed,
    CollectionPersonalMediaSourceDto? Source = null,
    string? Error = null);

/// <summary>
/// Coordinates Custom Collection personal-media sources without resolving or
/// persisting individual LocalAsset membership. Read projections are filtered
/// again for the current viewer by the persistence authorization query.
/// </summary>
public sealed class CollectionPersonalMediaService(
    ICollectionRepository collections,
    IProfileRepository profiles,
    IViewProfileRepository viewProfiles,
    IViewGalleryRepository galleries,
    ICollectionViewSourceRepository sources)
{
    private static readonly IReadOnlySet<string> ViewRuleFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "media_type", "file_type", "orientation", "duration", "captured_date",
        "people", "place", "device", "tags", "favorite", "owner", "source",
    };

    public async Task<IReadOnlyList<CollectionGalleryReferenceDto>> ListEligibleGalleriesAsync(
        Guid ownerProfileId,
        CancellationToken ct = default)
    {
        var policy = await viewProfiles.GetPolicyAsync(ownerProfileId, ct);
        if (!policy.ViewEnabled)
            return [];

        var owned = await galleries.GetOwnedAsync(ownerProfileId, ct);
        return owned.Select(gallery => new CollectionGalleryReferenceDto(
                gallery.Id,
                gallery.Name,
                gallery.Kind == ViewGalleryKind.Manual ? "manual" : "smart",
                gallery.UpdatedAt))
            .ToList();
    }

    public async Task<CollectionPersonalMediaListResult> ListForViewerAsync(
        Guid collectionId,
        Guid viewerProfileId,
        CancellationToken ct = default)
    {
        var collection = await collections.GetByIdAsync(collectionId, ct);
        if (collection is null)
            return new(false, false, []);

        var viewer = await profiles.GetByIdAsync(viewerProfileId, ct);
        if (!CollectionAccessPolicy.CanAccess(collection, viewer))
            return new(true, false, []);

        var projection = await sources.GetAuthorizedProjectionAsync([collectionId], viewerProfileId, ct);
        return new(true, true, projection.Select(ToDto).ToList());
    }

    public async Task<CollectionPersonalMediaWriteResult> AddAsync(
        Guid collectionId,
        Guid ownerProfileId,
        CollectionPersonalMediaSourceWriteRequest request,
        CancellationToken ct = default)
    {
        var access = await CanEditCustomCollectionAsync(collectionId, ownerProfileId, ct);
        if (!access.Found || !access.Allowed)
            return new(access.Found, access.Allowed);

        if (!TryValidateRequest(request, out var kind, out var rule, out var error))
            return new(true, true, Error: error);

        try
        {
            var source = kind == CollectionViewSourceKind.Gallery
                ? await sources.AddGalleryAsync(new(collectionId, ownerProfileId, request.GalleryId!.Value, request.Position), ct)
                : await sources.AddSmartRuleAsync(new(collectionId, ownerProfileId, rule!, request.Position), ct);
            return new(true, true, ToDto(source));
        }
        catch (InvalidOperationException exception)
        {
            return new(true, true, Error: exception.Message);
        }
    }

    public async Task<CollectionPersonalMediaWriteResult> UpdateAsync(
        Guid collectionId,
        Guid sourceId,
        Guid ownerProfileId,
        CollectionPersonalMediaSourceWriteRequest request,
        CancellationToken ct = default)
    {
        var access = await CanEditCustomCollectionAsync(collectionId, ownerProfileId, ct);
        if (!access.Found || !access.Allowed)
            return new(access.Found, access.Allowed);

        if (!TryValidateRequest(request, out var kind, out var rule, out var error))
            return new(true, true, Error: error);

        try
        {
            var source = await sources.UpdateAsync(new(
                sourceId,
                collectionId,
                ownerProfileId,
                kind,
                kind == CollectionViewSourceKind.Gallery ? request.GalleryId : null,
                kind == CollectionViewSourceKind.SmartRule ? rule : null,
                request.Position), ct);
            return source is null
                ? new(false, true)
                : new(true, true, ToDto(source));
        }
        catch (InvalidOperationException exception)
        {
            return new(true, true, Error: exception.Message);
        }
    }

    public async Task<CollectionPersonalMediaWriteResult> RemoveAsync(
        Guid collectionId,
        Guid sourceId,
        Guid ownerProfileId,
        CancellationToken ct = default)
    {
        var access = await CanEditCustomCollectionAsync(collectionId, ownerProfileId, ct);
        if (!access.Found || !access.Allowed)
            return new(access.Found, access.Allowed);

        try
        {
            var removed = await sources.RemoveAsync(collectionId, sourceId, ownerProfileId, ct);
            return new(removed, true);
        }
        catch (InvalidOperationException exception)
        {
            return new(true, true, Error: exception.Message);
        }
    }

    private async Task<(bool Found, bool Allowed)> CanEditCustomCollectionAsync(
        Guid collectionId,
        Guid ownerProfileId,
        CancellationToken ct)
    {
        var collection = await collections.GetByIdAsync(collectionId, ct);
        if (collection is null)
            return (false, false);
        var owner = await profiles.GetByIdAsync(ownerProfileId, ct);
        return (true,
            collection.CollectionType == CollectionType.Custom
            && owner?.Role == ProfileRole.Administrator
            && CollectionAccessPolicy.CanEdit(collection, owner));
    }

    public static bool TryValidateRequest(
        CollectionPersonalMediaSourceWriteRequest request,
        out CollectionViewSourceKind kind,
        out ViewSmartRuleDefinition? rule,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        kind = default;
        rule = null;
        error = null;

        if (request.AdditionalMembers is { Count: > 0 })
        {
            error = "Personal-media sources accept only a Gallery reference or a versioned View rule; individual asset IDs are not supported.";
            return false;
        }
        if (request.Position < 0)
        {
            error = "position cannot be negative.";
            return false;
        }

        if (string.Equals(request.Kind, CollectionPersonalMediaSourceKinds.Gallery, StringComparison.Ordinal))
        {
            kind = CollectionViewSourceKind.Gallery;
            if (!request.GalleryId.HasValue || request.GalleryId.Value == Guid.Empty
                || request.RuleDefinition is not null
                || request.RuleVersion.HasValue)
            {
                error = "A Gallery source requires gallery_id and cannot include a View rule.";
                return false;
            }
            return true;
        }

        if (!string.Equals(request.Kind, CollectionPersonalMediaSourceKinds.SmartRule, StringComparison.Ordinal))
        {
            error = "kind must be gallery or smart_rule.";
            return false;
        }

        kind = CollectionViewSourceKind.SmartRule;
        if (request.GalleryId.HasValue
            || request.RuleVersion != ViewSmartRuleDefinition.CurrentVersion
            || request.RuleDefinition is null
            || request.RuleDefinition.Version != ViewSmartRuleDefinition.CurrentVersion)
        {
            error = $"A smart View source requires rule_version {ViewSmartRuleDefinition.CurrentVersion} and one matching rule_definition.";
            return false;
        }

        var conditions = request.RuleDefinition.Groups.SelectMany(group => group.Conditions).ToList();
        if (conditions.Count == 0
            || conditions.Any(condition =>
                !ViewRuleFields.Contains(condition.Field)
                || string.IsNullOrWhiteSpace(condition.Op)
                || !IsValueFreeOperator(condition.Op)
                   && string.IsNullOrWhiteSpace(condition.Value)
                   && condition.Values is not { Length: > 0 }))
        {
            error = "A smart View source requires at least one complete condition from the View rule field catalog.";
            return false;
        }

        try
        {
            rule = ViewSmartRuleDefinition.Create(
                request.RuleVersion.Value,
                JsonSerializer.Serialize(request.RuleDefinition));
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool IsValueFreeOperator(string value) =>
        value is "known" or "unknown" or "has_any_value" or "has_no_known_value";

    private static CollectionPersonalMediaSourceDto ToDto(CollectionViewSource source) => new(
        source.Id,
        source.CollectionId,
        source.OwnerProfileId,
        ToContractKind(source.Kind),
        source.GalleryId,
        source.SmartRule?.Version,
        DeserializeRule(source.SmartRule?.Json),
        source.Position);

    private static CollectionPersonalMediaSourceDto ToDto(CollectionViewSourceProjection source) => new(
        source.SourceId,
        source.CollectionId,
        source.OwnerProfileId,
        ToContractKind(source.Kind),
        source.GalleryId,
        source.RuleVersion,
        DeserializeRule(source.RuleJson),
        source.Position);

    private static string ToContractKind(CollectionViewSourceKind kind) => kind switch
    {
        CollectionViewSourceKind.Gallery => CollectionPersonalMediaSourceKinds.Gallery,
        CollectionViewSourceKind.SmartRule => CollectionPersonalMediaSourceKinds.SmartRule,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static CollectionRuleDefinitionDto? DeserializeRule(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<CollectionRuleDefinitionDto>(json);
}
