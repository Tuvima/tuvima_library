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
///                          a TV show, a comic series, a book series).
///                          For TV this resolves to the SHOW,
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
public static class ClaimScopeCatalog
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
            [BridgeIdKeys.ComicVineId]               = ClaimScope.Parent,

            // Container-level descriptive fields.
            [MetadataFieldConstants.Album]             = ClaimScope.Parent,
            [MetadataFieldConstants.ShowName]          = ClaimScope.Parent,
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
                [BridgeIdKeys.WikidataQid]         = ClaimScope.Parent,  // resolved QID is the album, not the track
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
                [MetadataFieldConstants.FictionalUniverse] = ClaimScope.Parent,
                [MetadataFieldConstants.Characters]        = ClaimScope.Parent,
                [MetadataFieldConstants.NarrativeLocation] = ClaimScope.Parent,
                // director stays Self — different per episode
            },
            [MediaType.Comics] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]       = ClaimScope.Parent,
                [MetadataFieldConstants.Illustrator]  = ClaimScope.Parent,
                [MetadataFieldConstants.Genre]        = ClaimScope.Parent,
                [MetadataFieldConstants.Description]  = ClaimScope.Parent,
                [MetadataFieldConstants.Cover]        = ClaimScope.Self,
                [MetadataFieldConstants.CoverUrl]     = ClaimScope.Self,
                [MetadataFieldConstants.FictionalUniverse] = ClaimScope.Parent,
                [MetadataFieldConstants.Characters]        = ClaimScope.Parent,
                [MetadataFieldConstants.NarrativeLocation] = ClaimScope.Parent,
            },
            [MediaType.Books] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]      = ClaimScope.Parent,
                [MetadataFieldConstants.Genre]       = ClaimScope.Parent,
                [MetadataFieldConstants.FictionalUniverse] = ClaimScope.Parent,
                [MetadataFieldConstants.Characters]        = ClaimScope.Parent,
                [MetadataFieldConstants.NarrativeLocation] = ClaimScope.Parent,
            },
            [MediaType.Audiobooks] = new(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataFieldConstants.Author]      = ClaimScope.Parent,
                [MetadataFieldConstants.Narrator]    = ClaimScope.Parent,
                [MetadataFieldConstants.Genre]       = ClaimScope.Parent,
                [MetadataFieldConstants.FictionalUniverse] = ClaimScope.Parent,
                [MetadataFieldConstants.Characters]        = ClaimScope.Parent,
                [MetadataFieldConstants.NarrativeLocation] = ClaimScope.Parent,
            },
            [MediaType.Movies] = new(StringComparer.OrdinalIgnoreCase)
            {
                // Movies are standalone — TargetForParentScope collapses to
                // the movie's own Work id. Declaring these fields Parent-scoped
                // routes them to works.id (rather than media_assets.id), which
                // gives every reader a single uniform lookup target.
                [MetadataFieldConstants.Year]        = ClaimScope.Parent,
                [MetadataFieldConstants.Description] = ClaimScope.Parent,
                [MetadataFieldConstants.Genre]       = ClaimScope.Parent,
                [MetadataFieldConstants.Cover]       = ClaimScope.Parent,
                [MetadataFieldConstants.CoverUrl]    = ClaimScope.Parent,
                [MetadataFieldConstants.CastMember]  = ClaimScope.Parent,
                [MetadataFieldConstants.Director]    = ClaimScope.Parent,
                [MetadataFieldConstants.Runtime]     = ClaimScope.Parent,
                [MetadataFieldConstants.FictionalUniverse] = ClaimScope.Parent,
                [MetadataFieldConstants.Characters]        = ClaimScope.Parent,
                [MetadataFieldConstants.NarrativeLocation] = ClaimScope.Parent,
            },
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

        var lookupKey = BridgeIdKeys.All.Contains(claimKey)
            ? claimKey
            : StripCompanionQidSuffix(claimKey);

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
    /// Returns the set of claim keys that are <see cref="ClaimScope.Parent"/>
    /// for the given media type. Used by reader queries (LibraryItemRepository,
    /// SearchIndexRepository, CollectionRuleEvaluator) to know which canonical fields
    /// must be looked up on the parent Work id rather than the asset id.
    ///
    /// The set is the union of <see cref="DefaultMap"/> Parent entries and
    /// the per-media-type override Parent entries. Companion QID keys are
    /// included automatically (e.g. if <c>genre</c> is parent-scoped, so is
    /// <c>genre_qid</c>).
    /// </summary>
    public static IReadOnlySet<string> GetParentScopedKeys(MediaType mediaType)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, scope) in DefaultMap)
        {
            if (scope == ClaimScope.Parent)
                set.Add(key);
        }

        if (Overrides.TryGetValue(mediaType, out var ovr))
        {
            foreach (var (key, scope) in ovr)
            {
                if (scope == ClaimScope.Parent)
                    set.Add(key);
                else
                    set.Remove(key); // override demoted a default Parent → Self
            }
        }

        // Mirror companion QID keys for any multi-valued parent key.
        var withCompanions = new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
        foreach (var key in set)
        {
            if (MetadataFieldConstants.MultiValuedKeys.Contains(key))
                withCompanions.Add(key + MetadataFieldConstants.CompanionQidSuffix);
        }
        return withCompanions;
    }

    /// <summary>
    /// Convenience predicate equivalent to
    /// <c>GetScope(claimKey, mediaType) == ClaimScope.Parent</c>.
    /// </summary>
    public static bool IsParentScoped(string claimKey, MediaType mediaType)
        => GetScope(claimKey, mediaType) == ClaimScope.Parent;

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
