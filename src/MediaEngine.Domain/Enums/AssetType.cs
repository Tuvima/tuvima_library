namespace MediaEngine.Domain.Enums;

/// <summary>
/// Classifies image assets stored in the <c>entity_assets</c> table.
/// Every entity type (Work, Person, Universe, FictionalEntity) shares
/// the same set of asset type slots — providers fill what they can,
/// users upload the rest.
/// </summary>
public enum AssetType
{
    /// <summary>Primary cover art (book cover, movie poster, album art).</summary>
    CoverArt,

    /// <summary>Person headshot or character portrait.</summary>
    Headshot,

    /// <summary>Wide promotional banner image.</summary>
    Banner,

    /// <summary>Square promotional image used when a dedicated square crop is preferred.</summary>
    SquareArt,

    /// <summary>Transparent title treatment or faction logo.</summary>
    Logo,

    /// <summary>Cinematic background image (movie or show background art).</summary>
    Background,

    /// <summary>Season-level poster art for television or episodic media.</summary>
    SeasonPoster,

    /// <summary>Season-level thumbnail or wide season still.</summary>
    SeasonThumb,

    /// <summary>Episode-specific still image.</summary>
    EpisodeStill,

    /// <summary>Actor-in-costume or animated character portrait for a specific performer-character pair.</summary>
    CharacterPortrait,
}
