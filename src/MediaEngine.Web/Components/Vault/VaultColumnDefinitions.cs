namespace MediaEngine.Web.Components.Vault;

/// <summary>Defines a single column in the configurable Vault table.</summary>
public sealed class VaultColumnDef
{
    /// <summary>Unique key for this column (e.g. "title", "author", "pipeline").</summary>
    public required string Key { get; init; }

    /// <summary>Display header label.</summary>
    public required string Label { get; init; }

    /// <summary>CSS width (e.g. "40%", "120px", "auto").</summary>
    public string Width { get; init; } = "auto";

    /// <summary>Text alignment: "left", "center", "right".</summary>
    public string Align { get; init; } = "left";

    /// <summary>Whether this column is visible by default.</summary>
    public bool DefaultVisible { get; init; } = true;

    /// <summary>Whether this column can be sorted.</summary>
    public bool Sortable { get; init; }

    /// <summary>Sort parameter key (maps to API sort param).</summary>
    public string? SortKey { get; init; }

    /// <summary>Column render type — determines which template the table uses.</summary>
    public ColumnRenderType RenderType { get; init; } = ColumnRenderType.Text;

    /// <summary>Property path on VaultItemViewModel to read value from (for Text/Date columns).</summary>
    public string? PropertyName { get; init; }
}

/// <summary>How the column should be rendered.</summary>
public enum ColumnRenderType
{
    /// <summary>Plain text from PropertyName.</summary>
    Text,
    /// <summary>The media cell: thumbnail + title + subtitle + creator.</summary>
    MediaCell,
    /// <summary>Pipeline stage dots (3 StageGate components).</summary>
    Pipeline,
    /// <summary>Status pill component.</summary>
    StatusPill,
    /// <summary>Universe link text.</summary>
    UniverseLink,
    /// <summary>Date formatted as relative time.</summary>
    Date,
    /// <summary>Duration formatted as h:mm:ss.</summary>
    Duration,
    /// <summary>File size formatted as human-readable.</summary>
    FileSize,
    /// <summary>Rating stars or numeric display.</summary>
    Rating,
    /// <summary>Checkbox for batch selection (always first column).</summary>
    Checkbox,
    /// <summary>Person avatar + name cell.</summary>
    PersonCell,
    /// <summary>Role chips.</summary>
    RoleChips,
    /// <summary>Presence count with media type pills.</summary>
    PresenceCount,
    /// <summary>Universe icon + name cell.</summary>
    UniverseCell,
    /// <summary>Numeric count display.</summary>
    Count,
    /// <summary>Media type breakdown text.</summary>
    MediaBreakdown,
}

/// <summary>Provides default column definitions per media type and tab.</summary>
public static class VaultColumnDefinitions
{
    private static VaultColumnDef Checkbox() => new()
    {
        Key = "checkbox",
        Label = "",
        Width = "48px",
        Align = "center",
        DefaultVisible = true,
        Sortable = false,
        RenderType = ColumnRenderType.Checkbox,
    };

    /// <summary>
    /// Returns the column definitions for the Media tab, filtered by media type.
    /// Pass null or an empty string to get the "All types" default set.
    /// </summary>
    public static List<VaultColumnDef> GetMediaColumns(string? mediaType) =>
        (mediaType?.ToLowerInvariant()) switch
        {
            "books" or "book" => BooksColumns(),
            "audiobooks" or "audiobook" => AudiobooksColumns(),
            "movies" or "movie" => MoviesColumns(),
            "tv" => TvColumns(),
            "music" => MusicColumns(),
            "comics" or "comic" => ComicsColumns(),
            "podcasts" or "podcast" => PodcastsColumns(),
            _ => AllMediaColumns(),
        };

    // ── All / mixed ──────────────────────────────────────────────────────────

    private static List<VaultColumnDef> AllMediaColumns() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Title",    Width = "40%", RenderType = ColumnRenderType.MediaCell },
        new() { Key = "pipeline", Label = "Pipeline", Width = "12%", RenderType = ColumnRenderType.Pipeline },
        new() { Key = "universe", Label = "Universe", Width = "18%", RenderType = ColumnRenderType.UniverseLink },
        new() { Key = "status",   Label = "Status",   Width = "20%", RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Books ─────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> BooksColumns() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Title",   Width = "35%", RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author",   Label = "Author",  Width = "auto", Sortable = true, SortKey = "author",  RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "series",   Label = "Series",  Width = "auto", Sortable = true, SortKey = "series",  RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",     Label = "Year",    Width = "80px", Sortable = true, SortKey = "year",    RenderType = ColumnRenderType.Text, PropertyName = "Year",   Align = "center" },
        new() { Key = "pipeline", Label = "Pipeline", Width = "12%", RenderType = ColumnRenderType.Pipeline },
        new() { Key = "status",   Label = "Status",  Width = "15%", RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Audiobooks ────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> AudiobooksColumns() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Title",    Width = "30%", RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author",   Label = "Author",   Width = "auto", Sortable = true, SortKey = "author",   RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "narrator", Label = "Narrator", Width = "auto", Sortable = true, SortKey = "narrator", RenderType = ColumnRenderType.Text, PropertyName = "Narrator" },
        new() { Key = "series",   Label = "Series",   Width = "auto", Sortable = true, SortKey = "series",   RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "duration", Label = "Duration", Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "pipeline", Label = "Pipeline", Width = "12%", RenderType = ColumnRenderType.Pipeline },
        new() { Key = "status",   Label = "Status",   Width = "12%", RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Movies ────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> MoviesColumns() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Title",    Width = "30%", RenderType = ColumnRenderType.MediaCell },
        new() { Key = "director", Label = "Director", Width = "auto", Sortable = true, SortKey = "director", RenderType = ColumnRenderType.Text, PropertyName = "Director" },
        new() { Key = "year",     Label = "Year",     Width = "80px", Sortable = true, SortKey = "year",     RenderType = ColumnRenderType.Text, PropertyName = "Year",   Align = "center" },
        new() { Key = "duration", Label = "Duration", Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "rating",   Label = "Rating",   Width = "80px", RenderType = ColumnRenderType.Rating,  Align = "center" },
        new() { Key = "pipeline", Label = "Pipeline", Width = "12%", RenderType = ColumnRenderType.Pipeline },
        new() { Key = "status",   Label = "Status",   Width = "12%", RenderType = ColumnRenderType.StatusPill },
    ];

    // ── TV ────────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> TvColumns() =>
    [
        Checkbox(),
        new() { Key = "media",   Label = "Title",   Width = "35%", RenderType = ColumnRenderType.MediaCell },
        new() { Key = "season",  Label = "Season",  Width = "80px", Sortable = true, SortKey = "season",  RenderType = ColumnRenderType.Text, PropertyName = "Season",  Align = "center" },
        new() { Key = "episode", Label = "Episode", Width = "80px", Sortable = true, SortKey = "episode", RenderType = ColumnRenderType.Text, PropertyName = "Episode", Align = "center" },
        new() { Key = "year",    Label = "Year",    Width = "80px", Sortable = true, SortKey = "year",    RenderType = ColumnRenderType.Text, PropertyName = "Year",    Align = "center" },
        new() { Key = "pipeline", Label = "Pipeline", Width = "12%", RenderType = ColumnRenderType.Pipeline },
        new() { Key = "status",  Label = "Status",  Width = "12%", RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Music ─────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> MusicColumns() =>
    [
        Checkbox(),
        new() { Key = "media",       Label = "Title",    Width = "30%", RenderType = ColumnRenderType.MediaCell },
        new() { Key = "artist",      Label = "Artist",   Width = "auto", Sortable = true, SortKey = "artist", RenderType = ColumnRenderType.Text, PropertyName = "Artist" },
        new() { Key = "album",       Label = "Album",    Width = "auto", Sortable = true, SortKey = "album",  RenderType = ColumnRenderType.Text, PropertyName = "Album" },
        new() { Key = "tracknumber", Label = "Track #",  Width = "60px", RenderType = ColumnRenderType.Text,  PropertyName = "TrackNumber", Align = "center" },
        new() { Key = "duration",    Label = "Duration", Width = "80px", RenderType = ColumnRenderType.Duration },
        new() { Key = "pipeline",    Label = "Pipeline", Width = "10%",  RenderType = ColumnRenderType.Pipeline },
        new() { Key = "status",      Label = "Status",   Width = "10%",  RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Comics ────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> ComicsColumns() =>
    [
        Checkbox(),
        new() { Key = "media",       Label = "Title",   Width = "35%", RenderType = ColumnRenderType.MediaCell },
        new() { Key = "series",      Label = "Series",  Width = "auto", Sortable = true, SortKey = "series",      RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "issuenumber", Label = "Issue #", Width = "60px", RenderType = ColumnRenderType.Text,  PropertyName = "IssueNumber", Align = "center" },
        new() { Key = "year",        Label = "Year",    Width = "80px", Sortable = true, SortKey = "year",        RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "pipeline",    Label = "Pipeline", Width = "12%", RenderType = ColumnRenderType.Pipeline },
        new() { Key = "status",      Label = "Status",  Width = "15%", RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Podcasts ──────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> PodcastsColumns() =>
    [
        Checkbox(),
        new() { Key = "media",         Label = "Title",      Width = "35%", RenderType = ColumnRenderType.MediaCell },
        new() { Key = "podcastname",   Label = "Podcast",    Width = "auto", Sortable = true, SortKey = "podcast",       RenderType = ColumnRenderType.Text, PropertyName = "PodcastName" },
        new() { Key = "episodenumber", Label = "Episode #",  Width = "60px", RenderType = ColumnRenderType.Text,  PropertyName = "EpisodeNumber", Align = "center" },
        new() { Key = "duration",      Label = "Duration",   Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "pipeline",      Label = "Pipeline",   Width = "12%",  RenderType = ColumnRenderType.Pipeline },
        new() { Key = "status",        Label = "Status",     Width = "12%",  RenderType = ColumnRenderType.StatusPill },
    ];

    // ── People tab ────────────────────────────────────────────────────────────

    /// <summary>Returns the column definitions for the People tab.</summary>
    public static List<VaultColumnDef> GetPeopleColumns() =>
    [
        Checkbox(),
        new() { Key = "person",   Label = "Name",     Width = "60%", RenderType = ColumnRenderType.PersonCell },
        new() { Key = "roles",    Label = "Roles",    Width = "20%", RenderType = ColumnRenderType.RoleChips },
        new() { Key = "presence", Label = "In Library", Width = "20%", Align = "right", RenderType = ColumnRenderType.PresenceCount },
    ];

    // ── Universes tab ─────────────────────────────────────────────────────────

    /// <summary>Returns the column definitions for the Universes tab.</summary>
    public static List<VaultColumnDef> GetUniverseColumns() =>
    [
        Checkbox(),
        new() { Key = "universe",       Label = "Universe",  Width = "40%", RenderType = ColumnRenderType.UniverseCell },
        new() { Key = "seriescount",    Label = "Series",    Width = "15%", Align = "center", RenderType = ColumnRenderType.Count,          PropertyName = "SeriesCount" },
        new() { Key = "mediabreakdown", Label = "Media",     Width = "25%", Align = "center", RenderType = ColumnRenderType.MediaBreakdown },
        new() { Key = "peoplecount",    Label = "People",    Width = "15%", Align = "center", RenderType = ColumnRenderType.Count,          PropertyName = "PeopleCount" },
    ];

    // ── Hubs tab ──────────────────────────────────────────────────────────────

    /// <summary>Returns the column definitions for the Hubs tab.</summary>
    public static List<VaultColumnDef> GetHubColumns() =>
    [
        Checkbox(),
        new() { Key = "name",      Label = "Name",     Width = "40%", RenderType = ColumnRenderType.Text,      PropertyName = "Name" },
        new() { Key = "type",      Label = "Type",     Width = "15%", RenderType = ColumnRenderType.Text,      PropertyName = "Type" },
        new() { Key = "itemcount", Label = "Items",    Width = "15%", Align = "center", RenderType = ColumnRenderType.Count, PropertyName = "ItemCount" },
        new() { Key = "status",    Label = "Status",   Width = "15%", RenderType = ColumnRenderType.StatusPill },
        new() { Key = "featured",  Label = "Featured", Width = "10%", Align = "center", RenderType = ColumnRenderType.Text, PropertyName = "Featured" },
    ];
}
