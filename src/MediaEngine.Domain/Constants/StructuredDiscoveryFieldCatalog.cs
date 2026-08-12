using System.Collections.Frozen;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Constants;

public enum DiscoveryValueKind
{
    Text,
    Number,
    Entity,
    State,
}

public enum DiscoveryFactSource
{
    StructuredProvider,
    LocalAi,
    LibraryState,
}

public sealed record StructuredDiscoveryFieldDefinition(
    string Key,
    string Label,
    string Category,
    DiscoveryValueKind ValueKind,
    DiscoveryFactSource Source,
    bool IsMultiValued,
    ClaimScope Scope,
    string? WikidataProperty = null,
    IReadOnlySet<MediaType>? MediaTypes = null)
{
    public bool IsApplicable(MediaType mediaType) =>
        MediaTypes is null || MediaTypes.Count == 0 || MediaTypes.Contains(mediaType);
}

/// <summary>
/// Product-facing source of truth for factual discovery fields and subjective AI attributes.
/// Runtime provider configuration is validated against this catalog by tests/startup validation.
/// </summary>
public static class StructuredDiscoveryFieldCatalog
{
    public const string CapabilityId = MediaEngine.Domain.Entities.CapabilityId.EnrichmentStructuredDiscoveryMetadata;
    public const string CapabilityVersion = "1.0";

    private static readonly IReadOnlySet<MediaType> Watch = Set(MediaType.Movies, MediaType.TV);
    private static readonly IReadOnlySet<MediaType> Listen = Set(MediaType.Music);

    public static IReadOnlyList<StructuredDiscoveryFieldDefinition> Fields { get; } =
    [
        Entity(MetadataFieldConstants.AwardReceived, "Award won", "Recognition", "P166"),
        Entity(MetadataFieldConstants.AwardNominated, "Award nominated", "Recognition", "P1411"),
        Entity(MetadataFieldConstants.AwardFamily, "Award family", "Recognition"),
        Entity(MetadataFieldConstants.NominationFamily, "Nomination family", "Recognition"),
        Entity(MetadataFieldConstants.CountryOfOrigin, "Country of origin", "Media", "P495"),
        Entity(MetadataFieldConstants.Language, "Language", "Media", "P407"),
        Entity(MetadataFieldConstants.OriginalLanguage, "Original language", "Media", "P364"),
        Entity(MetadataFieldConstants.ProductionCompany, "Production company", "People & Organizations", "P272", Watch),
        Entity(MetadataFieldConstants.Network, "Network / broadcaster", "People & Organizations", "P449", Set(MediaType.TV)),
        Entity(MetadataFieldConstants.PublisherField, "Publisher", "People & Organizations", "P123",
            Set(MediaType.Books, MediaType.Comics, MediaType.Audiobooks)),
        Entity(MetadataFieldConstants.RecordLabel, "Record label", "People & Organizations", "P264", Listen),
        Entity(MetadataFieldConstants.NarrativeLocation, "Narrative location", "Story & World", "P840"),
        Entity(MetadataFieldConstants.SetInPeriod, "Set in period", "Story & World", "P2408"),
        Entity(MetadataFieldConstants.MainSubject, "Main subject", "Story & World", "P921"),
        Entity(MetadataFieldConstants.BasedOn, "Based on", "Story & World", "P144"),
        Entity(MetadataFieldConstants.FilmingLocation, "Filming location", "Production", "P915", Watch),

        Ai("themes", "Theme"),
        Ai("mood", "Mood"),
        Ai("vibe", "Vibe"),
        Ai("content_warnings", "Content warning"),
    ];

    public static IReadOnlyDictionary<string, StructuredDiscoveryFieldDefinition> ByKey { get; } =
        Fields.ToFrozenDictionary(field => field.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, StructuredDiscoveryFieldDefinition> ByWikidataProperty { get; } =
        Fields.Where(field => field.WikidataProperty is not null)
            .ToFrozenDictionary(field => field.WikidataProperty!, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> ExistingEntityBackedKeys = new[]
    {
        MetadataFieldConstants.Author,
        MetadataFieldConstants.Narrator,
        MetadataFieldConstants.Director,
        MetadataFieldConstants.CastMember,
        MetadataFieldConstants.VoiceActor,
        MetadataFieldConstants.Artist,
        MetadataFieldConstants.Composer,
        MetadataFieldConstants.Screenwriter,
        MetadataFieldConstants.Illustrator,
        MetadataFieldConstants.Characters,
        MetadataFieldConstants.FictionalUniverse,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string key, out StructuredDiscoveryFieldDefinition definition) =>
        ByKey.TryGetValue(key, out definition!);

    public static bool IsEntityBacked(string key) =>
        (TryGet(key, out var definition) && definition.ValueKind == DiscoveryValueKind.Entity)
        || ExistingEntityBackedKeys.Contains(key);

    private static StructuredDiscoveryFieldDefinition Entity(
        string key,
        string label,
        string category,
        string? property = null,
        IReadOnlySet<MediaType>? mediaTypes = null) =>
        new(key, label, category, DiscoveryValueKind.Entity, DiscoveryFactSource.StructuredProvider,
            IsMultiValued: true, ClaimScope.Parent, property, mediaTypes);

    private static StructuredDiscoveryFieldDefinition Ai(string key, string label) =>
        new(key, label, "AI Attributes", DiscoveryValueKind.Text, DiscoveryFactSource.LocalAi,
            IsMultiValued: true, ClaimScope.Parent);

    private static IReadOnlySet<MediaType> Set(params MediaType[] values) =>
        values.ToFrozenSet();
}
