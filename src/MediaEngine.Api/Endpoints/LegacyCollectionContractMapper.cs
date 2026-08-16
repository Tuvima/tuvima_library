using MediaEngine.Contracts.Collections;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Constants;

namespace MediaEngine.Api.Endpoints;

internal static class LegacyCollectionContractMapper
{
    public static CollectionDto ToContract(this Collection source) => new()
    {
        Id = source.Id,
        UniverseId = source.UniverseId,
        DisplayName = source.DisplayName,
        ParentCollectionId = source.ParentCollectionId,
        UniverseStatus = source.UniverseStatus.ToStorageValue(),
        CreatedAt = source.CreatedAt,
        Works = source.Works.Select(ToContract).ToList(),
    };

    public static WorkDto ToContract(this Work source) => new()
    {
        Id = source.Id,
        CollectionId = source.CollectionId,
        MediaType = source.MediaType.ToString(),
        Ordinal = source.Ordinal,
        UniverseMismatch = source.UniverseMismatch,
        UniverseMismatchAt = source.UniverseMismatchAt,
        CanonicalValues = source.CanonicalValues.Select(value => new CanonicalValueDto
        {
            Key = value.Key,
            Value = value.Value,
            LastScoredAt = value.LastScoredAt,
        }).ToList(),
    };

    public static SeriesManifestViewDto ToContract(this MediaEngine.Domain.Models.SeriesManifestViewDto source) => new()
    {
        CollectionId = source.CollectionId,
        SeriesQid = source.SeriesQid,
        SeriesLabel = source.SeriesLabel,
        LastHydratedAt = source.LastHydratedAt,
        ContainerKind = source.ContainerKind,
        ExpectedTotal = source.ExpectedTotal,
        ExpectedTotalKind = source.ExpectedTotalKind,
        ExpectedTotalSource = source.ExpectedTotalSource,
        ExpectedTotalConfidence = source.ExpectedTotalConfidence,
        TotalCount = source.TotalCount,
        OwnedCount = source.OwnedCount,
        MissingCount = source.MissingCount,
        ProvisionalCount = source.ProvisionalCount,
        AmbiguousCount = source.AmbiguousCount,
        SupplementaryCount = source.SupplementaryCount,
        CollectedContentCount = source.CollectedContentCount,
        UnpositionedCount = source.UnpositionedCount,
        AuthoritativeTotalsByContainer = source.AuthoritativeTotalsByContainer,
        Warnings = source.Warnings.Select(warning => new SeriesManifestWarningDto
        {
            Code = warning.Code,
            Message = warning.Message,
            Qid = warning.Qid,
        }).ToList(),
        Items = source.Items.Select(item => new SeriesManifestItemDto
        {
            Id = item.Id,
            ItemQid = item.ItemQid,
            SeriesQid = item.SeriesQid,
            ItemLabel = item.ItemLabel,
            ItemDescription = item.ItemDescription,
            MediaType = item.MediaType,
            MediaKind = item.MediaKind,
            InstanceOfQids = item.InstanceOfQids,
            RawOrdinal = item.RawOrdinal,
            ParsedOrdinal = item.ParsedOrdinal,
            OrdinalScopeQid = item.OrdinalScopeQid,
            SortOrder = item.SortOrder,
            PublicationDate = item.PublicationDate,
            Duration = item.Duration,
            ParentCollectionQid = item.ParentCollectionQid,
            ParentCollectionLabel = item.ParentCollectionLabel,
            IsCollection = item.IsCollection,
            IsExpandedFromCollection = item.IsExpandedFromCollection,
            MembershipScope = item.MembershipScope,
            OrderSource = item.OrderSource,
            OwnershipState = item.OwnershipState,
            LinkedWorkId = item.LinkedWorkId,
        }).ToList(),
    };

    public static MediaEngine.Domain.Models.CollectionRulePredicate ToDomain(this CollectionRulePredicateDto source) => new()
    {
        Field = source.Field,
        Op = source.Op,
        Value = source.Value,
        DisplayValue = source.DisplayValue,
        Values = source.Values,
    };

    public static MediaEngine.Domain.Models.CollectionRuleDefinition ToDomain(this CollectionRuleDefinitionDto source) => new()
    {
        Version = source.Version,
        Groups = source.Groups.Select(group => new MediaEngine.Domain.Models.CollectionRuleGroup
        {
            Id = group.Id,
            JoinWithPrevious = group.JoinWithPrevious,
            MatchMode = group.MatchMode,
            Conditions = group.Conditions.Select(ToDomain).ToList(),
        }).ToList(),
    };
}
