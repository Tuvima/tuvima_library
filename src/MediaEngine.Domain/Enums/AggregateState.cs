namespace MediaEngine.Domain.Enums;

/// <summary>Lifecycle state for a Work's Wikidata identity.</summary>
public enum WikidataLinkStatus
{
    Pending,
    Confirmed,
    Skipped,
    Missing,
    Manual,
    ProviderOnly,
    AutoAligned,
    UserConfirmed,
    UserReplaced,
    UserRejected,
}

/// <summary>Specificity of a Work's resolved external identity.</summary>
public enum WorkMatchLevel
{
    Work,
    Edition,
    RetailOnly,
    Unlinked,
}

/// <summary>Structural purpose of a Collection.</summary>
public enum CollectionType
{
    Universe,
    Series,
    ContentGroup,
    System,
    Smart,
    Playlist,
    PlaylistFolder,
    Mix,
    Genre,
    Author,
    Collection,
    Custom,
}

/// <summary>Ownership boundary for a Collection.</summary>
public enum CollectionScope
{
    Library,
    User,
}

/// <summary>How multiple collection rules are combined.</summary>
public enum CollectionMatchMode
{
    All,
    Any,
}

/// <summary>Direction used to order collection results.</summary>
public enum CollectionSortDirection
{
    Asc,
    Desc,
}

/// <summary>Coverage state for a Collection's universe identity.</summary>
public enum CollectionUniverseStatus
{
    Unknown,
    None,
    Limited,
    Rich,
    Complete,
}
