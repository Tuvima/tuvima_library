namespace MediaEngine.Contracts.Details;

public sealed class DetailPageViewModel
{
    public string Id { get; init; } = string.Empty;
    public DetailEntityType EntityType { get; init; }
    public DetailPresentationContext PresentationContext { get; init; }
    public DetailEditorTarget? EditorTarget { get; init; }

    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Tagline { get; init; }
    public string? Description { get; init; }
    public DescriptionAttributionViewModel? DescriptionAttribution { get; init; }
    public PersonDetailFacts? PersonDetails { get; init; }

    public ArtworkSet Artwork { get; init; } = new();
    public HeroBrandViewModel? HeroBrand { get; init; }
    public ProgressViewModel? Progress { get; init; }

    public IReadOnlyList<OwnedFormatViewModel> OwnedFormats { get; init; } = [];
    public MultiFormatState MultiFormatState { get; init; } = MultiFormatState.SingleFormat;
    public ReadingListeningSyncViewModel? ReadingListeningSync { get; init; }
    public ReadingListeningSyncCapabilityViewModel? SyncCapability { get; init; }
    public SeriesPlacementViewModel? SeriesPlacement { get; init; }

    public IReadOnlyList<MetadataPill> Metadata { get; init; } = [];
    public IReadOnlyList<DetailAction> PrimaryActions { get; init; } = [];
    public IReadOnlyList<DetailAction> SecondaryActions { get; init; } = [];
    public IReadOnlyList<DetailAction> OverflowActions { get; init; } = [];

    public IReadOnlyList<CreditGroupViewModel> ContributorGroups { get; init; } = [];
    public IReadOnlyList<EntityCreditViewModel> PreviewContributors { get; init; } = [];
    public IReadOnlyList<CharacterGroupViewModel> CharacterGroups { get; init; } = [];
    public IReadOnlyList<EntityCreditViewModel> PreviewCharacters { get; init; } = [];

    public IReadOnlyList<RelationshipGroup> RelationshipStrip { get; init; } = [];
    public IReadOnlyList<DetailTab> Tabs { get; init; } = [];
    public IReadOnlyList<MediaGroupingViewModel> MediaGroups { get; init; } = [];

    public CanonicalIdentityStatus IdentityStatus { get; init; } = CanonicalIdentityStatus.Unknown;
    public LibraryStatus LibraryStatus { get; init; } = LibraryStatus.Unknown;
    public bool IsAdminView { get; init; }
}

public sealed class DetailEditorTarget
{
    public string EntityId { get; init; } = string.Empty;
    public string EntityKind { get; init; } = "Work";
    public string ContainerMode { get; init; } = "Canonical";
    public string? InitialTab { get; init; }
    public string? InitialScope { get; init; }
}

public sealed class DescriptionAttributionViewModel
{
    public string SourceName { get; init; } = string.Empty;
    public string? SourceUrl { get; init; }
    public string LicenseName { get; init; } = string.Empty;
    public string? LicenseUrl { get; init; }
    public string Notice { get; init; } = string.Empty;
}

public enum DetailEntityType
{
    Work,
    Movie,
    MovieSeries,
    TvShow,
    TvSeason,
    TvEpisode,
    Book,
    BookSeries,
    Audiobook,
    ComicIssue,
    ComicSeries,
    MusicAlbum,
    MusicArtist,
    MusicTrack,
    Person,
    Character,
    Universe,
    Collection
}

public enum DetailPresentationContext
{
    Default,
    Listen,
    Watch,
    Read,
    Comics,
    Admin,
    Search,
    Universe
}

public sealed class ArtworkSet
{
    public string? BackdropUrl { get; init; }
    public string? BannerUrl { get; init; }
    public string? PosterUrl { get; init; }
    public string? CoverUrl { get; init; }
    public string? LogoUrl { get; init; }
    public string? PortraitUrl { get; init; }
    public string? CharacterImageUrl { get; init; }
    public IReadOnlyList<string> RelatedArtworkUrls { get; init; } = [];

    public IReadOnlyList<string> DominantColors { get; init; } = [];
    public string? PrimaryColor { get; init; }
    public string? SecondaryColor { get; init; }
    public string? AccentColor { get; init; }

    public HeroArtworkViewModel HeroArtwork { get; init; } = new();
    public ArtworkPresentationMode PresentationMode { get; init; } = ArtworkPresentationMode.GeneratedIdentity;
    public ArtworkSource Source { get; init; } = ArtworkSource.Generated;
}

public sealed class HeroArtworkViewModel
{
    public string? Url { get; init; }
    public HeroArtworkMode Mode { get; init; } = HeroArtworkMode.Placeholder;
    public bool HasImage { get; init; }
    public double? AspectRatio { get; init; }
    public string? BackgroundPosition { get; init; }
    public string? MobilePosition { get; init; }
}

public enum HeroArtworkMode
{
    BackdropWithLogo = 0,
    BackdropWithRenderedTitle = 1,
    ArtworkFallback = 2,
    Placeholder = 3,

    [System.Obsolete("Use BackdropWithLogo or BackdropWithRenderedTitle.")]
    Background = BackdropWithRenderedTitle,

    [System.Obsolete("Use ArtworkFallback.")]
    CoverFallback = ArtworkFallback
}

public sealed class HeroBrandViewModel
{
    public string? Label { get; init; }
    public string? ImageUrl { get; init; }
}

public enum ArtworkPresentationMode
{
    CinematicBackdrop,
    ColorGradientFromArtwork,
    PortraitEcho,
    CollageGradient,
    GeneratedIdentity,
    PairedEditionGradient
}

public enum ArtworkSource
{
    Unknown,
    User,
    Provider,
    Generated
}

public sealed class MetadataPill
{
    public string Label { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? Kind { get; init; }
    public string? Route { get; init; }
    public string? Tooltip { get; init; }
}

public sealed class DetailAction
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Icon { get; init; }
    public string? Route { get; init; }
    public string? Tooltip { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsDestructive { get; init; }
    public bool IsAdminOnly { get; init; }
    public bool IsDisabled { get; init; }
    public bool IsStub { get; init; }
    public string? DisplayStyle { get; init; }
    public IReadOnlyList<DetailAction> Children { get; init; } = [];
}

public sealed class DetailTab
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool IsAdminOnly { get; init; }
}

public sealed class PersonDetailFacts
{
    public string? WikidataQid { get; init; }
    public string? WikidataUrl { get; init; }
    public string? Biography { get; init; }
    public string? Occupation { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public string? DateOfBirth { get; init; }
    public string? DateOfDeath { get; init; }
    public string? PlaceOfBirth { get; init; }
    public string? PlaceOfDeath { get; init; }
    public string? Nationality { get; init; }
    public bool IsPseudonym { get; init; }
    public bool IsGroup { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? EnrichedAt { get; init; }
    public IReadOnlyList<PersonExternalLink> ExternalLinks { get; init; } = [];
    public IReadOnlyList<PersonRelatedLink> Aliases { get; init; } = [];
    public IReadOnlyList<PersonRelatedLink> GroupMembers { get; init; } = [];
    public IReadOnlyList<PersonRelatedLink> MemberOfGroups { get; init; } = [];
}

public sealed class PersonExternalLink
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? IconLabel { get; init; }
}

public sealed class PersonRelatedLink
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Route { get; init; }
}

public sealed class SeriesPlacementViewModel
{
    public string SeriesId { get; init; } = string.Empty;
    public string SeriesTitle { get; init; } = string.Empty;
    public string SelectedSeriesId { get; init; } = string.Empty;
    public bool CanChooseSeries { get; init; }
    public bool CanSetDefaultSeries { get; init; }
    public IReadOnlyList<SeriesOptionViewModel> AvailableSeries { get; init; } = [];
    public string? UniverseId { get; init; }
    public string? UniverseTitle { get; init; }
    public int? PositionNumber { get; init; }
    public int? TotalKnownItems { get; init; }
    public string? PositionLabel { get; init; }
    public SeriesOrderingType OrderingType { get; init; } = SeriesOrderingType.LibraryOrder;
    public SeriesItemViewModel? PreviousItem { get; init; }
    public SeriesItemViewModel CurrentItem { get; init; } = new();
    public SeriesItemViewModel? NextItem { get; init; }
    public IReadOnlyList<SeriesItemViewModel> OrderedItems { get; init; } = [];
}

public sealed class SeriesOptionViewModel
{
    public string SeriesId { get; init; } = string.Empty;
    public string SeriesTitle { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
    public bool IsDefault { get; init; }
    public string? MediaScope { get; init; }
}

public sealed class SetDefaultSeriesRequest
{
    public string SeriesId { get; init; } = string.Empty;
    public string? SeriesTitle { get; init; }
}

public enum SeriesOrderingType
{
    PublicationOrder,
    ReleaseOrder,
    StoryOrder,
    LibraryOrder,
    IssueNumber,
    VolumeNumber
}

public sealed class SeriesItemViewModel
{
    public string Id { get; init; } = string.Empty;
    public DetailEntityType EntityType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? ArtworkUrl { get; init; }
    public int? PositionNumber { get; init; }
    public string? PositionLabel { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsOwned { get; init; }
    public LibraryProgressState ProgressState { get; init; } = LibraryProgressState.Unknown;
}

public sealed class OwnedFormatViewModel
{
    public string Id { get; init; } = string.Empty;
    public MediaFormatType FormatType { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? CoverUrl { get; init; }
    public string? EditionTitle { get; init; }
    public string? Publisher { get; init; }
    public string? ReleaseDate { get; init; }
    public string? PrimaryContributor { get; init; }
    public string? FileFormat { get; init; }
    public string? Runtime { get; init; }
    public int? PageCount { get; init; }
    public int? ChapterCount { get; init; }
    public ProgressViewModel? Progress { get; init; }
    public IReadOnlyList<DetailAction> Actions { get; init; } = [];
}

public enum MediaFormatType
{
    Ebook,
    Audiobook,
    Paperback,
    Hardcover,
    ComicIssue,
    ComicVolume,
    Movie,
    TvSeries,
    MusicAlbum
}

public enum MultiFormatState
{
    SingleFormat,
    MultipleFormatsSeparateProgress,
    MultipleFormatsSyncAvailable,
    MultipleFormatsSyncEnabled,
    MultipleFormatsSyncUnavailable
}

public enum SyncCapabilityState
{
    NotApplicable,
    Unavailable,
    AvailableNotEnabled,
    Enabled,
    NeedsReview,
    Failed
}

public sealed class ReadingListeningSyncCapabilityViewModel
{
    public SyncCapabilityState State { get; init; } = SyncCapabilityState.NotApplicable;
    public string? Reason { get; init; }
    public double? EstimatedConfidence { get; init; }
    public string? TextEditionId { get; init; }
    public string? AudioEditionId { get; init; }
    public DetailAction? EnableAction { get; init; }
    public DetailAction? PreviewAction { get; init; }
    public DetailAction? SettingsAction { get; init; }
}

public sealed class ReadingListeningSyncViewModel
{
    public string WorkId { get; init; } = string.Empty;
    public string TextEditionId { get; init; } = string.Empty;
    public string AudioEditionId { get; init; } = string.Empty;
    public SyncCapabilityState Status { get; init; }
    public double Confidence { get; init; }
    public string? CurrentChapterTitle { get; init; }
    public double PercentComplete { get; init; }
    public TextPositionViewModel? TextPosition { get; init; }
    public AudioPositionViewModel? AudioPosition { get; init; }
    public IReadOnlyList<ChapterSyncRowViewModel> ChapterMap { get; init; } = [];
}

public sealed class ProgressViewModel
{
    public double Percent { get; init; }
    public string? Label { get; init; }
}

public sealed class TextPositionViewModel
{
    public string? Label { get; init; }
}

public sealed class AudioPositionViewModel
{
    public string? Label { get; init; }
}

public sealed class ChapterSyncRowViewModel
{
    public string Title { get; init; } = string.Empty;
    public string? TextPosition { get; init; }
    public string? AudioPosition { get; init; }
    public string? Status { get; init; }
}

public sealed class EntityCreditViewModel
{
    public string EntityId { get; init; } = string.Empty;
    public RelatedEntityType EntityType { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public string? FallbackInitials { get; init; }
    public string PrimaryRole { get; init; } = string.Empty;
    public string? SecondaryRole { get; init; }
    public string? CharacterName { get; init; }
    public string? CharacterEntityId { get; init; }
    public string? CharacterImageUrl { get; init; }
    public int SortOrder { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsCanonical { get; init; }
    public string? SourceName { get; init; }
    public string? SourceId { get; init; }
}

public enum RelatedEntityType
{
    Person,
    Character,
    Organization,
    Group,
    MusicArtist,
    Publisher,
    Label,
    Universe,
    Series
}

public sealed class CreditGroupViewModel
{
    public string Title { get; init; } = string.Empty;
    public CreditGroupType GroupType { get; init; }
    public IReadOnlyList<EntityCreditViewModel> Credits { get; init; } = [];
    public int DisplayPriority { get; init; }
    public bool IsInitiallyExpanded { get; init; } = true;
    public int InitialVisibleCount { get; init; } = 8;
}

public enum CreditGroupType
{
    Cast,
    Directors,
    Writers,
    Producers,
    Authors,
    Narrators,
    Translators,
    Editors,
    Illustrators,
    CreativeTeam,
    MusicCredits,
    RelatedPeople,
    Collaborators,
    Organizations,
    Publishers,
    Labels,
    PrimaryArtists,
    FeaturedArtists,
    BandMembers,
    RelatedArtists
}

public sealed class CharacterGroupViewModel
{
    public string Title { get; init; } = string.Empty;
    public CharacterGroupType GroupType { get; init; }
    public IReadOnlyList<EntityCreditViewModel> Characters { get; init; } = [];
}

public enum CharacterGroupType
{
    MainCharacters,
    SupportingCharacters,
    Villains,
    Teams,
    Factions,
    Houses,
    Organizations
}

public sealed class RelationshipGroup
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<RelatedEntityChip> Items { get; init; } = [];
}

public sealed class RelatedEntityChip
{
    public string Id { get; init; } = string.Empty;
    public RelatedEntityType EntityType { get; init; }
    public string Label { get; init; } = string.Empty;
    public string? Route { get; init; }
}

public sealed class MediaGroupingViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<MediaGroupingItemViewModel> Items { get; init; } = [];
}

public sealed class MediaGroupingItemViewModel
{
    public string Id { get; init; } = string.Empty;
    public DetailEntityType EntityType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? ArtworkUrl { get; init; }
    public string? TrackNumber { get; init; }
    public string? Duration { get; init; }
    public string? Artist { get; init; }
    public bool IsExplicit { get; init; }
    public string? Quality { get; init; }
    public double? ProgressPercent { get; init; }
    public IReadOnlyList<MetadataPill> Metadata { get; init; } = [];
    public IReadOnlyList<DetailAction> Actions { get; init; } = [];
    public bool IsOwned { get; init; } = true;
    public LibraryProgressState ProgressState { get; init; } = LibraryProgressState.Unknown;
}

public enum CanonicalIdentityStatus
{
    Unknown,
    LocalOnly,
    ProviderMatched,
    WikidataLinked,
    NeedsReview
}

public enum LibraryStatus
{
    Unknown,
    Owned,
    PartiallyOwned,
    Unowned,
    InProgress,
    Completed
}

public enum LibraryProgressState
{
    Unknown,
    Unstarted,
    InProgress,
    Completed,
    Missing,
    Unowned
}
