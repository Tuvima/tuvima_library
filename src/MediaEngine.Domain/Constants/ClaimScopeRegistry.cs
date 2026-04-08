using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Constants;

/// <summary>
/// Where a metadata claim or external identifier belongs in the
/// parent/child Work hierarchy.
///
/// • <see cref="Self"/>   — the asset's own Work (a track, episode, issue,
///                          single book, movie). This is the default for
///                          file-specific data: title, track number, runtime,
///                          ISBN, ISRC, Apple Music track id, etc.
/// • <see cref="Parent"/> — the topmost container above the asset (an album,
///                          a TV show, a comic series, a book series, a
///                          podcast show). For TV this resolves to the SHOW,
///                          not the season. For standalone media (movies,
///                          single-volume books) parent collapses to self.
/// </summary>
public enum ClaimScope
{
    Self,
    Parent,
}

/// <summary>
/// Single source of truth for which claim keys describe the asset's own
/// Work versus the container above it. Drives <c>WorkClaimRouter</c>, which
/// in turn drives the Retail and Wikidata workers' decisions about where
/// to store provider claims and bridge IDs.
///
/// The classification is media-type aware because the same key can mean
/// different things in different contexts:
///   • <c>year</c> on a music track → the album's release year (Parent)
///   • <c>year</c> on a movie       → the movie's release year (Self)
///   • <c>director</c> on a TV episode → per-episode director (Self)
///   • <c>director</c> on a movie      → the movie's director (Self)
///
/// New claim keys default to <see cref="ClaimScope.Self"/>. Add explicit
/// entries here when a key describes the container, not the file.
/// </summary>
public static class ClaimScopeRegistry
{
    // ── Default map (applied to every media type) ────────────────────────
    // Container-only identifiers and container-only descriptive fields
    // always route to Parent regardless of media type.
    private static readonly Dictionary<string, ClaimScope> DefaultMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Container bridge IDs — only meaningful at the album/show/series level.
            [BridgeIdKeys.AppleMusicCollectionId]    = ClaimScope.Parent,
            [BridgeIdKeys.AppleArtistId]             = ClaimScope.Parent,
            [BridgeIdKeys.MusicBrainzId]             = ClaimScope.Parent,
            [BridgeIdKeys.MusicBrainzReleaseGroupId] = ClaimScope.Parent,
            [BridgeIdKeys.TvdbId]                    = ClaimScope.Parent,
            [BridgeIdKeys.PodcastIndexId]            = ClaimScope.Parent,
            [BridgeIdKeys.ComicVineId]               = ClaimScope.Parent,

            // Container-level descriptive fields.
            [MetadataFieldConstants.Album]             = ClaimScope.Parent,
            [MetadataFieldConstants.ShowName]          = ClaimScope.Parent,
            [MetadataFieldConstants.PodcastName]       = ClaimScope.Parent,
            [MetadataFieldConstants.Series]            = ClaimScope.Parent,
            [MetadataFieldConstants.Franchise]         = ClaimScope.Parent,
            [MetadataFieldConstants.Network]           = ClaimScope.Parent,
            [MetadataFieldConstants.PublisherField]    = ClaimScope.Parent,
            [MetadataFieldConstants.SeasonCount]       = ClaimScope.Parent,
            [MetadataFieldConstants.EpisodeCount]      = ClaimScope.Parent,
            [MetadataFieldConstants.TrackCount]        = ClaimScope.Parent,
            [MetadataFieldConstants.IssueCount]        = ClaimScope.Parent,
            [MetadataFieldConstants.ChildEntitiesJson] = ClaimScope.Parent,
        };

    // ── Per-media-type overrides applied AFTER the default map. ──────────
    private static readonly Dictionary<MediaType, Dictionary<string, ClaimScope>> Overrides =
        new()
        {
            [MediaType.Music] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]      = ClaimScope.Parent,  // album artist
                [MetadataFieldConstants.Artist]      = ClaimScope.Parent,
                [MetadataFieldConstants.Year]        = ClaimScope.Parent,  // album release year
                [MetadataFieldConstants.Genre]       = ClaimScope.Parent,
                [MetadataFieldConstants.Description] = ClaimScope.Parent,  // album-level
                [MetadataFieldConstants.Cover]       = ClaimScope.Parent,  // album art
                [MetadataFieldConstants.CoverUrl]    = ClaimScope.Parent,
                [MetadataFieldConstants.Composer]    = ClaimScope.Parent,
            },
            [MediaType.TV] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]      = ClaimScope.Parent,  // showrunner
                [MetadataFieldConstants.Genre]       = ClaimScope.Parent,
                [MetadataFieldConstants.CastMember]  = ClaimScope.Parent,
                [MetadataFieldConstants.Year]        = ClaimScope.Parent,  // show start year
                [MetadataFieldConstants.Description] = ClaimScope.Parent,
                [MetadataFieldConstants.Cover]       = ClaimScope.Parent,
                [MetadataFieldConstants.CoverUrl]    = ClaimScope.Parent,
                // director stays Self — different per episode
            },
            [MediaType.Comics] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]       = ClaimScope.Parent,
                [MetadataFieldConstants.Illustrator]  = ClaimScope.Parent,
                [MetadataFieldConstants.Genre]        = ClaimScope.Parent,
                [MetadataFieldConstants.Description]  = ClaimScope.Parent,
                [MetadataFieldConstants.Cover]        = ClaimScope.Parent,
                [MetadataFieldConstants.CoverUrl]     = ClaimScope.Parent,
            },
            [MediaType.Books] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]      = ClaimScope.Parent,
                [MetadataFieldConstants.Genre]       = ClaimScope.Parent,
            },
            [MediaType.Audiobooks] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]      = ClaimScope.Parent,
                [MetadataFieldConstants.Narrator]    = ClaimScope.Parent,
                [MetadataFieldConstants.Genre]       = ClaimScope.Parent,
            },
            [MediaType.Podcasts] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]      = ClaimScope.Parent,  // host
                [MetadataFieldConstants.Genre]       = ClaimScope.Parent,
                [MetadataFieldConstants.Description] = ClaimScope.Parent,
                [MetadataFieldConstants.Cover]       = ClaimScope.Parent,
                [MetadataFieldConstants.CoverUrl]    = ClaimScope.Parent,
            },
            // Movies have no parent in the current resolver (always Standalone),
            // so no overrides are needed — TargetForParentScope falls back to
            // the movie's own Work.
        };

    /// <summary>
    /// Returns the scope for the given claim key under the given media
    /// type. Companion QID keys (e.g. <c>genre_qid</c>) inherit the scope
    /// of their primary key (<c>genre</c>).
    /// Unknown keys default to <see cref="ClaimScope.Self"/>.
    /// </summary>
    public static ClaimScope GetScope(string claimKey, MediaType mediaType)
    {
        if (string.IsNullOrEmpty(claimKey))
            return ClaimScope.Self;

        var lookupKey = StripCompanionQidSuffix(claimKey);

        // Per-media-type overrides take precedence over the default map.
        if (Overrides.TryGetValue(mediaType, out var ovr)
            && ovr.TryGetValue(lookupKey, out var overrideScope))
        {
            return overrideScope;
        }

        return DefaultMap.TryGetValue(lookupKey, out var defaultScope)
            ? defaultScope
            : ClaimScope.Self;
    }

    /// <summary>
    /// Strips the <c>_qid</c> suffix used for companion QID keys so the
    /// scope lookup matches the primary key.
    /// </summary>
    private static string StripCompanionQidSuffix(string key)
    {
        const string suffix = MetadataFieldConstants.CompanionQidSuffix;
        return key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? key[..^suffix.Length]
            : key;
    }
}
