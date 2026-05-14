using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Editing;

public enum SharedMediaEditorMode
{
    Normal,
    Review,
    Batch,
}

public enum MediaEditorIdentityIntent
{
    None,
    EditLocalDetails,
    FixRetailMatch,
    ConfirmRetailMatch,
    FixWikidataMatch,
    ConfirmWikidataMatch,
    MarkWikidataMissing,
    ReclassifyMediaType,
    ConfirmArtwork,
    ResolveWriteback,
}

public sealed class MediaEditorLaunchRequest
{
    public List<Guid> EntityIds { get; init; } = [];
    public Guid? LaunchEntityId { get; init; }
    public string? LaunchEntityKind { get; init; }
    public SharedMediaEditorMode Mode { get; init; } = SharedMediaEditorMode.Normal;
    public MediaEditorIdentityIntent IdentityIntent { get; init; } = MediaEditorIdentityIntent.None;
    public string? InitialScope { get; init; }
    public string? InitialTab { get; init; }
    public string? InitialCanonicalTargetGroup { get; init; }
    public Guid? ReviewItemId { get; init; }
    public string? ReviewTrigger { get; init; }
    public string? MediaType { get; init; }
    public string? HeaderTitle { get; init; }
    public string? HeaderSubtitle { get; init; }
    public string? CoverUrl { get; init; }
    public Func<Task>? OnArtworkChanged { get; init; }
    public List<MediaEditorPreviewItem> PreviewItems { get; init; } = [];
}

public sealed class MediaEditorPreviewItem
{
    public Guid EntityId { get; init; }
    public string Title { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string? MediaType { get; init; }
}

public sealed class MediaEditorSchema
{
    public required string MediaType { get; init; }
    public required string DefaultTargetGroup { get; init; }
    public required IReadOnlyList<(string Key, string Label)> QuickSearchTargets { get; init; }
    public required IReadOnlyList<MediaEditorFieldGroup> Groups { get; init; }
}

public sealed class MediaEditorFieldGroup
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string TabId { get; init; }
    public required IReadOnlyList<MediaEditorFieldDefinition> Fields { get; init; }
}

public sealed class MediaEditorFieldDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string InputKind { get; init; } = "text";
    public bool SupportsBatch { get; init; } = true;
    public bool IdentityField { get; init; }
    public string? Placeholder { get; init; }
}

public sealed record ReviewEditorTarget(
    string InitialTab,
    string CanonicalTargetGroup,
    string FocusField,
    string Summary,
    MediaEditorIdentityIntent Intent,
    string PrimaryActionLabel);

public static class ReviewTargetResolver
{
    public static ReviewEditorTarget Resolve(string? mediaType, string? reviewTrigger)
    {
        var normalizedType = NormalizeMediaType(mediaType);
        var normalizedTrigger = (reviewTrigger ?? string.Empty).Trim();
        var intent = ResolveIntent(normalizedTrigger);
        var initialTab = ResolveInitialTab(intent);
        var actionLabel = ResolvePrimaryActionLabel(intent);

        if (normalizedType == "Music")
        {
            return normalizedTrigger switch
            {
                "RetailMatchFailed" or "RetailMatchAmbiguous" or "AuthorityMatchFailed" or "ContentMatchFailed" or "QidNoMatch" or "MissingQid" or "WikidataBridgeFailed" or "MultipleQidMatches"
                    => new(initialTab, "album", "album", BuildSummary(intent, "album and artist"), intent, actionLabel),
                _ => new(initialTab, "album", "album", BuildSummary(intent, "music"), intent, actionLabel),
            };
        }

        if (normalizedType == "Audiobooks")
        {
            return normalizedTrigger switch
            {
                "QidNoMatch" or "MissingQid" or "WikidataBridgeFailed" or "MultipleQidMatches"
                    => new(initialTab, "narrator", "narrator", BuildSummary(intent, "narrator"), intent, actionLabel),
                _ => new(initialTab, "audiobook_identity", "title", BuildSummary(intent, "audiobook"), intent, actionLabel),
            };
        }

        if (normalizedType == "TV")
        {
            return new(initialTab, "show_episode", "show_name", BuildSummary(intent, "show and episode"), intent, actionLabel);
        }

        if (normalizedType == "Comics")
        {
            return new(initialTab, "issue", "series", BuildSummary(intent, "series and issue"), intent, actionLabel);
        }

        if (normalizedType == "Movies")
        {
            return new(initialTab, "movie_identity", "title", BuildSummary(intent, "movie"), intent, actionLabel);
        }

        return new(initialTab, "book_identity", "title", BuildSummary(intent, "item"), intent, actionLabel);
    }

    public static string NormalizeMediaType(string? mediaType) =>
        (mediaType ?? string.Empty).Trim() switch
        {
            "Book" => "Books",
            "Comic" => "Comics",
            "" => "Books",
            var value => value,
        };

    private static MediaEditorIdentityIntent ResolveIntent(string trigger) =>
        trigger switch
        {
            "RetailMatchAmbiguous" => MediaEditorIdentityIntent.ConfirmRetailMatch,
            "RetailMatchFailed" or "AuthorityMatchFailed" or "ContentMatchFailed" or "UserFixMatch"
                => MediaEditorIdentityIntent.FixRetailMatch,
            "WikidataBridgeFailed" or "MultipleQidMatches" or "QidNoMatch"
                => MediaEditorIdentityIntent.FixWikidataMatch,
            "MissingQid" => MediaEditorIdentityIntent.MarkWikidataMissing,
            "AmbiguousMediaType" or "RootWatchFolder" => MediaEditorIdentityIntent.ReclassifyMediaType,
            "ArtworkUnconfirmed" => MediaEditorIdentityIntent.ConfirmArtwork,
            "WritebackFailed" => MediaEditorIdentityIntent.ResolveWriteback,
            "MetadataConflict" or "LowConfidence" => MediaEditorIdentityIntent.EditLocalDetails,
            _ => MediaEditorIdentityIntent.EditLocalDetails,
        };

    private static string ResolveInitialTab(MediaEditorIdentityIntent intent) =>
        intent switch
        {
            MediaEditorIdentityIntent.FixRetailMatch or
            MediaEditorIdentityIntent.ConfirmRetailMatch or
            MediaEditorIdentityIntent.FixWikidataMatch or
            MediaEditorIdentityIntent.ConfirmWikidataMatch or
            MediaEditorIdentityIntent.MarkWikidataMissing => "links",
            MediaEditorIdentityIntent.ConfirmArtwork => "artwork",
            MediaEditorIdentityIntent.ResolveWriteback => "file",
            _ => "details",
        };

    private static string ResolvePrimaryActionLabel(MediaEditorIdentityIntent intent) =>
        intent switch
        {
            MediaEditorIdentityIntent.FixRetailMatch => "Find Retail Match",
            MediaEditorIdentityIntent.ConfirmRetailMatch => "Confirm Retail Match",
            MediaEditorIdentityIntent.FixWikidataMatch => "Fix Wikidata Match",
            MediaEditorIdentityIntent.ConfirmWikidataMatch => "Choose Wikidata Match",
            MediaEditorIdentityIntent.MarkWikidataMissing => "Mark Provider-Only",
            MediaEditorIdentityIntent.ReclassifyMediaType => "Change Media Type",
            MediaEditorIdentityIntent.ConfirmArtwork => "Review Artwork",
            MediaEditorIdentityIntent.ResolveWriteback => "Retry Writeback",
            _ => "Review Metadata",
        };

    private static string BuildSummary(MediaEditorIdentityIntent intent, string subject) =>
        intent switch
        {
            MediaEditorIdentityIntent.FixRetailMatch => $"Find the correct retail match for this {subject}.",
            MediaEditorIdentityIntent.ConfirmRetailMatch => $"Confirm the retail match for this {subject}.",
            MediaEditorIdentityIntent.FixWikidataMatch => $"Fix the Wikidata identity for this {subject}.",
            MediaEditorIdentityIntent.MarkWikidataMissing => $"Keep the retail match and mark this {subject} as provider-only.",
            MediaEditorIdentityIntent.ReclassifyMediaType => "Confirm the correct media type before matching continues.",
            MediaEditorIdentityIntent.ConfirmArtwork => "Review artwork and choose the preferred assets.",
            MediaEditorIdentityIntent.ResolveWriteback => "Review the file write-back failure and retry or skip it.",
            _ => $"Review the {subject} metadata.",
        };
}

public static class MediaEditorSchemaCatalog
{
    public static MediaEditorSchema Resolve(string? mediaType)
    {
        var normalized = ReviewTargetResolver.NormalizeMediaType(mediaType);
        return normalized switch
        {
            "Music" => Music,
            "Movies" => Movies,
            "TV" => Tv,
            "Audiobooks" => Audiobooks,
            "Comics" => Comics,
            _ => Books,
        };
    }

    public static IReadOnlyList<MediaEditorFieldDefinition> ResolveBatchFields(IEnumerable<string> mediaTypes)
    {
        var normalized = mediaTypes
            .Select(ReviewTargetResolver.NormalizeMediaType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 1)
        {
            return Resolve(normalized[0]).Groups
                .Where(group => group.TabId is "details" or "options" or "sorting")
                .SelectMany(group => group.Fields)
                .Where(field => field.SupportsBatch)
                .DistinctBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return
        [
            new() { Key = "title", Label = "Title" },
            new() { Key = "year", Label = "Year" },
            new() { Key = "genre", Label = "Genre" },
            new() { Key = "language", Label = "Language" },
            new() { Key = "rating", Label = "Rating" },
        ];
    }

    public static IReadOnlyDictionary<string, string> BuildValueMap(
        LibraryItemDetailViewModel? detail,
        IEnumerable<CanonicalFieldViewModel> canonicals)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string? FindCanonicalValue(IEnumerable<CanonicalFieldViewModel> source, string key) =>
            source.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

        static void Add(IDictionary<string, string> target, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                target[key] = string.Equals(key, "description", StringComparison.OrdinalIgnoreCase)
                    ? NormalizeDescriptionParagraphs(value)
                    : value.Trim();
            }
        }

        static string NormalizeDescriptionParagraphs(string value)
        {
            var normalized = value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .TrimEnd();

            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            return string.Join("\n\n",
                normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph)));
        }

        foreach (var field in canonicals)
            Add(values, field.Key, field.Value);

        if (detail is null)
            return values;

        Add(values, "title", detail.Title);
        Add(values, "author", detail.Author);
        Add(values, "director", detail.Director);
        Add(values, "cast_member", detail.Cast);
        Add(values, "language", detail.Language);
        Add(values, "genre", detail.Genre);
        Add(values, "runtime", detail.Runtime);
        Add(values, "description", detail.Description);
        Add(values, "series", detail.Series);
        Add(values, "series_position", detail.SeriesPosition);
        Add(values, "narrator", detail.Narrator);
        Add(values, "rating", detail.Rating);
        Add(values, "wikidata_qid", detail.WikidataQid);
        Add(values, "file_name", detail.FileName);
        Add(values, "file_path", detail.FilePath);
        Add(values, "content_hash", detail.ContentHash);
        Add(values, "artist", FindCanonicalValue(canonicals, "artist"));
        Add(values, "album", FindCanonicalValue(canonicals, "album"));
        Add(values, "album_artist", FindCanonicalValue(canonicals, "album_artist"));
        Add(values, "composer", FindCanonicalValue(canonicals, "composer"));
        Add(values, "track_number", FindCanonicalValue(canonicals, "track_number"));
        Add(values, "disc_number", FindCanonicalValue(canonicals, "disc_number"));
        Add(values, "duration", FindCanonicalValue(canonicals, "duration"));
        Add(values, "lyrics", FindCanonicalValue(canonicals, "lyrics"));
        Add(values, "show_name", FindCanonicalValue(canonicals, "show_name"));
        Add(values, "season_number", FindCanonicalValue(canonicals, "season_number"));
        Add(values, "episode_number", FindCanonicalValue(canonicals, "episode_number"));
        Add(values, "episode_title", FindCanonicalValue(canonicals, "episode_title"));
        Add(values, "network", FindCanonicalValue(canonicals, "network"));
        Add(values, "subtitle", FindCanonicalValue(canonicals, "subtitle"));
        Add(values, "publisher", FindCanonicalValue(canonicals, "publisher"));
        Add(values, "studio", FindCanonicalValue(canonicals, "studio"));
        Add(values, "volume", FindCanonicalValue(canonicals, "volume"));
        Add(values, "illustrator", FindCanonicalValue(canonicals, "illustrator"));
        Add(values, "edition", FindCanonicalValue(canonicals, "edition"));
        Add(values, "year", detail.Year ?? FindCanonicalValue(canonicals, "year"));
        Add(values, "original_title", FindCanonicalValue(canonicals, "original_title"));
        Add(values, "sort_title", FindCanonicalValue(canonicals, "sort_title"));
        Add(values, "sort_artist", FindCanonicalValue(canonicals, "sort_artist"));
        Add(values, "sort_album", FindCanonicalValue(canonicals, "sort_album"));
        Add(values, "sort_series", FindCanonicalValue(canonicals, "sort_series"));
        Add(values, "comment", FindCanonicalValue(canonicals, "comment"));
        return values;
    }

    public static IReadOnlyList<string> GetStrongFieldKeys(string targetGroup) =>
        targetGroup switch
        {
            "album" => ["artist", "album"],
            "artist" => ["artist"],
            "track" => ["artist", "album", "title", "track_number"],
            "movie_identity" => ["title", "year", "director"],
            "show_episode" => ["show_name", "season_number", "episode_number", "episode_title"],
            "show" => ["show_name"],
            "book_identity" => ["title", "author"],
            "audiobook_identity" => ["title", "author", "narrator"],
            "narrator" => ["narrator"],
            "series" => ["series"],
            "issue" => ["title", "series", "series_position"],
            _ => ["title"],
        };

    private static readonly MediaEditorSchema Music = new()
    {
        MediaType = "Music",
        DefaultTargetGroup = "album",
        QuickSearchTargets = [("album", "Album"), ("artist", "Artist"), ("track", "Track")],
        Groups =
        [
            Group("music_details", "Details", "details",
                Field("title", "Title", identity: true),
                Field("year", "Year"),
                Field("description", "Description", "textarea", identity: true),
                Field("artist", "Artist", identity: true),
                Field("album", "Album", identity: true),
                Field("track_number", "Track"),
                Field("disc_number", "Disc"),
                Field("composer", "Composer"),
                Field("genre", "Genre")),
            Group("music_options", "Options", "options",
                Field("album_artist", "Album Artist"),
                Field("duration", "Duration"),
                Field("lyrics", "Lyrics", "textarea", batch: false),
                Field("language", "Language"),
                Field("rating", "Rating"),
                Field("comment", "Comment", "textarea")),
            Group("music_sorting", "Sorting", "sorting",
                Field("sort_title", "Sort Title"),
                Field("sort_artist", "Sort Artist"),
                Field("sort_album", "Sort Album")),
        ],
    };

    private static readonly MediaEditorSchema Movies = new()
    {
        MediaType = "Movies",
        DefaultTargetGroup = "movie_identity",
        QuickSearchTargets = [("movie_identity", "Movie")],
        Groups =
        [
            Group("movie_details", "Details", "details",
                Field("title", "Title", identity: true),
                Field("year", "Year", identity: true),
                Field("description", "Description", "textarea", identity: true),
                Field("original_title", "Original Title"),
                Field("director", "Director", identity: true),
                Field("runtime", "Runtime"),
                Field("studio", "Studio"),
                Field("language", "Language")),
            Group("movie_options", "Options", "options",
                Field("edition", "Edition"),
                Field("genre", "Genre"),
                Field("rating", "Rating"),
                Field("comment", "Comment", "textarea")),
            Group("movie_sorting", "Sorting", "sorting",
                Field("sort_title", "Sort Title")),
        ],
    };

    private static readonly MediaEditorSchema Tv = new()
    {
        MediaType = "TV",
        DefaultTargetGroup = "show_episode",
        QuickSearchTargets = [("show_episode", "Show / Episode"), ("show", "Show")],
        Groups =
        [
            Group("tv_details", "Details", "details",
                Field("show_name", "Show", identity: true),
                Field("year", "Year"),
                Field("description", "Description", "textarea", identity: true),
                Field("season_number", "Season", identity: true),
                Field("episode_number", "Episode", identity: true),
                Field("episode_title", "Episode Title"),
                Field("network", "Network"),
                Field("runtime", "Runtime")),
            Group("tv_options", "Options", "options",
                Field("genre", "Genre"),
                Field("rating", "Rating"),
                Field("language", "Language"),
                Field("comment", "Comment", "textarea")),
            Group("tv_sorting", "Sorting", "sorting",
                Field("sort_title", "Sort Episode Title"),
                Field("sort_series", "Sort Show")),
        ],
    };

    private static readonly MediaEditorSchema Books = new()
    {
        MediaType = "Books",
        DefaultTargetGroup = "book_identity",
        QuickSearchTargets = [("book_identity", "Book")],
        Groups =
        [
            Group("book_details", "Details", "details",
                Field("title", "Title", identity: true),
                Field("year", "Year"),
                Field("description", "Description", "textarea", identity: true),
                Field("subtitle", "Subtitle"),
                Field("author", "Author", identity: true),
                Field("series", "Series"),
                Field("series_position", "Series Number"),
                Field("publisher", "Publisher"),
                Field("language", "Language")),
            Group("book_options", "Options", "options",
                Field("genre", "Genre"),
                Field("rating", "Rating"),
                Field("comment", "Comment", "textarea")),
            Group("book_sorting", "Sorting", "sorting",
                Field("sort_title", "Sort Title"),
                Field("sort_series", "Sort Series")),
        ],
    };

    private static readonly MediaEditorSchema Audiobooks = new()
    {
        MediaType = "Audiobooks",
        DefaultTargetGroup = "narrator",
        QuickSearchTargets = [("narrator", "Narrator"), ("audiobook_identity", "Audiobook")],
        Groups =
        [
            Group("audiobook_details", "Details", "details",
                Field("title", "Title", identity: true),
                Field("year", "Year"),
                Field("description", "Description", "textarea", identity: true),
                Field("author", "Author", identity: true),
                Field("narrator", "Narrator", identity: true),
                Field("series", "Series"),
                Field("series_position", "Series Number"),
                Field("duration", "Duration"),
                Field("publisher", "Publisher")),
            Group("audiobook_options", "Options", "options",
                Field("genre", "Genre"),
                Field("language", "Language"),
                Field("rating", "Rating"),
                Field("comment", "Comment", "textarea")),
            Group("audiobook_sorting", "Sorting", "sorting",
                Field("sort_title", "Sort Title"),
                Field("sort_series", "Sort Series")),
        ],
    };

    private static readonly MediaEditorSchema Comics = new()
    {
        MediaType = "Comics",
        DefaultTargetGroup = "issue",
        QuickSearchTargets = [("issue", "Issue")],
        Groups =
        [
            Group("comic_details", "Details", "details",
                Field("title", "Title"),
                Field("year", "Year"),
                Field("description", "Description", "textarea", identity: true),
                Field("series", "Series", identity: true),
                Field("series_position", "Issue Number", identity: true),
                Field("author", "Writer"),
                Field("illustrator", "Artist"),
                Field("publisher", "Publisher")),
            Group("comic_options", "Options", "options",
                Field("volume", "Volume"),
                Field("genre", "Genre"),
                Field("comment", "Comment", "textarea")),
            Group("comic_sorting", "Sorting", "sorting",
                Field("sort_title", "Sort Title"),
                Field("sort_series", "Sort Series")),
        ],
    };

    private static MediaEditorFieldDefinition Field(
        string key,
        string label,
        string kind = "text",
        bool batch = true,
        bool identity = false) =>
        new()
        {
            Key = key,
            Label = label,
            InputKind = kind,
            SupportsBatch = batch,
            IdentityField = identity,
        };

    private static MediaEditorFieldGroup Group(
        string id,
        string label,
        string tabId,
        params MediaEditorFieldDefinition[] fields) =>
        new()
        {
            Id = id,
            Label = label,
            TabId = tabId,
            Fields = fields,
        };
}
