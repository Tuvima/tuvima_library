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
    /// <summary>Pipeline stage dots (4 StageGate components: File, Retail, Wikidata, Universe).</summary>
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
    /// <summary>Colored media type chip (e.g. "Book", "Movie").</summary>
    TypeBadge,
    /// <summary>Container cell: thumbnail + name + creator subtitle (for content group rows).</summary>
    ContainerCell,
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
        new() { Key = "media",       Label = "Title",      Width = "40%", Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "pipeline",    Label = "Pipeline",   Width = "12%", Sortable = false, Align = "center",           RenderType = ColumnRenderType.Pipeline },
        new() { Key = "resolution",  Label = "Resolution", Width = "15%", Sortable = true,  SortKey = "resolution_path", RenderType = ColumnRenderType.Text, PropertyName = "ResolutionSummary", DefaultVisible = false },
        new() { Key = "universe",    Label = "Universe",   Width = "18%", Sortable = true, SortKey = "universe",        RenderType = ColumnRenderType.UniverseLink },
        new() { Key = "status",      Label = "Status",     Width = "20%", Sortable = true, SortKey = "status",          RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Books ─────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> BooksColumns() =>
    [
        Checkbox(),
        new() { Key = "media",      Label = "Title",      Width = "35%", Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author",     Label = "Author",     Width = "auto", Sortable = true, SortKey = "author",          RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "series",     Label = "Series",     Width = "auto", Sortable = true, SortKey = "series",          RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",       Label = "Year",       Width = "80px", Sortable = true, SortKey = "year",            RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "pipeline",   Label = "Pipeline",   Width = "12%",  Sortable = false, Align = "center",           RenderType = ColumnRenderType.Pipeline },
        new() { Key = "resolution", Label = "Resolution", Width = "15%",  Sortable = true,  SortKey = "resolution_path", RenderType = ColumnRenderType.Text, PropertyName = "ResolutionSummary", DefaultVisible = false },
        new() { Key = "status",     Label = "Status",     Width = "15%",  Sortable = true, SortKey = "status",          RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Audiobooks ────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> AudiobooksColumns() =>
    [
        Checkbox(),
        new() { Key = "media",      Label = "Title",      Width = "30%", Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author",     Label = "Author",     Width = "auto", Sortable = true, SortKey = "author",          RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "narrator",   Label = "Narrator",   Width = "auto", Sortable = true, SortKey = "narrator",        RenderType = ColumnRenderType.Text, PropertyName = "Narrator" },
        new() { Key = "series",     Label = "Series",     Width = "auto", Sortable = true, SortKey = "series",          RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "duration",   Label = "Duration",   Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "pipeline",   Label = "Pipeline",   Width = "12%",  Sortable = false, Align = "center",           RenderType = ColumnRenderType.Pipeline },
        new() { Key = "resolution", Label = "Resolution", Width = "15%",  Sortable = true,  SortKey = "resolution_path", RenderType = ColumnRenderType.Text, PropertyName = "ResolutionSummary", DefaultVisible = false },
        new() { Key = "status",     Label = "Status",     Width = "12%",  Sortable = true, SortKey = "status",          RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Movies ────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> MoviesColumns() =>
    [
        Checkbox(),
        new() { Key = "media",      Label = "Title",      Width = "30%", Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "director",   Label = "Director",   Width = "auto", Sortable = true, SortKey = "director",        RenderType = ColumnRenderType.Text, PropertyName = "Director" },
        new() { Key = "year",       Label = "Year",       Width = "80px", Sortable = true, SortKey = "year",            RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "duration",   Label = "Duration",   Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "rating",     Label = "Rating",     Width = "80px", RenderType = ColumnRenderType.Rating, Align = "center" },
        new() { Key = "pipeline",   Label = "Pipeline",   Width = "12%",  Sortable = false, Align = "center",           RenderType = ColumnRenderType.Pipeline },
        new() { Key = "resolution", Label = "Resolution", Width = "15%",  Sortable = true,  SortKey = "resolution_path", RenderType = ColumnRenderType.Text, PropertyName = "ResolutionSummary", DefaultVisible = false },
        new() { Key = "status",     Label = "Status",     Width = "12%",  Sortable = true, SortKey = "status",          RenderType = ColumnRenderType.StatusPill },
    ];

    // ── TV ────────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> TvColumns() =>
    [
        Checkbox(),
        new() { Key = "media",      Label = "Title",      Width = "35%", Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "season",     Label = "Season",     Width = "80px", Sortable = true, SortKey = "season",          RenderType = ColumnRenderType.Text, PropertyName = "Season",  Align = "center" },
        new() { Key = "episode",    Label = "Episode",    Width = "80px", Sortable = true, SortKey = "episode",         RenderType = ColumnRenderType.Text, PropertyName = "Episode", Align = "center" },
        new() { Key = "year",       Label = "Year",       Width = "80px", Sortable = true, SortKey = "year",            RenderType = ColumnRenderType.Text, PropertyName = "Year",    Align = "center" },
        new() { Key = "pipeline",   Label = "Pipeline",   Width = "12%",  Sortable = false, Align = "center",           RenderType = ColumnRenderType.Pipeline },
        new() { Key = "resolution", Label = "Resolution", Width = "15%",  Sortable = true,  SortKey = "resolution_path", RenderType = ColumnRenderType.Text, PropertyName = "ResolutionSummary", DefaultVisible = false },
        new() { Key = "status",     Label = "Status",     Width = "12%",  Sortable = true, SortKey = "status",          RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Music ─────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> MusicColumns() =>
    [
        Checkbox(),
        new() { Key = "media",       Label = "Title",      Width = "30%", Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "artist",      Label = "Artist",     Width = "auto", Sortable = true, SortKey = "artist",          RenderType = ColumnRenderType.Text, PropertyName = "Artist" },
        new() { Key = "album",       Label = "Album",      Width = "20%", Sortable = true, SortKey = "album",           RenderType = ColumnRenderType.Text, PropertyName = "Album" },
        new() { Key = "tracknumber", Label = "Track #",    Width = "60px", RenderType = ColumnRenderType.Text,            PropertyName = "TrackNumber", Align = "center" },
        new() { Key = "duration",    Label = "Duration",   Width = "80px", RenderType = ColumnRenderType.Duration },
        new() { Key = "pipeline",    Label = "Pipeline",   Width = "10%",  Sortable = false, Align = "center",           RenderType = ColumnRenderType.Pipeline },
        new() { Key = "resolution",  Label = "Resolution", Width = "15%",  Sortable = true,  SortKey = "resolution_path", RenderType = ColumnRenderType.Text, PropertyName = "ResolutionSummary", DefaultVisible = false },
        new() { Key = "status",      Label = "Status",     Width = "10%",  Sortable = true, SortKey = "status",          RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Comics ────────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> ComicsColumns() =>
    [
        Checkbox(),
        new() { Key = "media",       Label = "Title",      Width = "35%", Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "series",      Label = "Series",     Width = "auto", Sortable = true, SortKey = "series",          RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "issuenumber", Label = "Issue #",    Width = "60px", RenderType = ColumnRenderType.Text,            PropertyName = "IssueNumber", Align = "center" },
        new() { Key = "year",        Label = "Year",       Width = "80px", Sortable = true, SortKey = "year",            RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "pipeline",    Label = "Pipeline",   Width = "12%",  Sortable = false, Align = "center",           RenderType = ColumnRenderType.Pipeline },
        new() { Key = "resolution",  Label = "Resolution", Width = "15%",  Sortable = true,  SortKey = "resolution_path", RenderType = ColumnRenderType.Text, PropertyName = "ResolutionSummary", DefaultVisible = false },
        new() { Key = "status",      Label = "Status",     Width = "15%",  Sortable = true, SortKey = "status",          RenderType = ColumnRenderType.StatusPill },
    ];

    // ── Podcasts ──────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> PodcastsColumns() =>
    [
        Checkbox(),
        new() { Key = "media",         Label = "Title",      Width = "35%", Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "podcastname",   Label = "Podcast",    Width = "auto", Sortable = true, SortKey = "podcast",         RenderType = ColumnRenderType.Text, PropertyName = "PodcastName" },
        new() { Key = "episodenumber", Label = "Episode #",  Width = "60px", RenderType = ColumnRenderType.Text,            PropertyName = "EpisodeNumber", Align = "center" },
        new() { Key = "duration",      Label = "Duration",   Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "pipeline",      Label = "Pipeline",   Width = "12%",  Sortable = false, Align = "center",           RenderType = ColumnRenderType.Pipeline },
        new() { Key = "resolution",    Label = "Resolution", Width = "15%",  Sortable = true,  SortKey = "resolution_path", RenderType = ColumnRenderType.Text, PropertyName = "ResolutionSummary", DefaultVisible = false },
        new() { Key = "status",        Label = "Status",     Width = "12%",  Sortable = true, SortKey = "status",          RenderType = ColumnRenderType.StatusPill },
    ];

    // ── People tab ────────────────────────────────────────────────────────────

    /// <summary>Returns the column definitions for the People tab.</summary>
    public static List<VaultColumnDef> GetPeopleColumns() =>
    [
        Checkbox(),
        new() { Key = "person",   Label = "Name",       Width = "60%", Sortable = true, SortKey = "name",     RenderType = ColumnRenderType.PersonCell },
        new() { Key = "roles",    Label = "Roles",      Width = "20%", RenderType = ColumnRenderType.RoleChips },
        new() { Key = "presence", Label = "In Library", Width = "20%", Align = "right", Sortable = true, SortKey = "presence", RenderType = ColumnRenderType.PresenceCount },
    ];

    // ── Universes tab ─────────────────────────────────────────────────────────

    /// <summary>Returns the column definitions for the Universes tab.</summary>
    public static List<VaultColumnDef> GetUniverseColumns() =>
    [
        Checkbox(),
        new() { Key = "universe",       Label = "Universe",  Width = "40%", Sortable = true, SortKey = "name",         RenderType = ColumnRenderType.UniverseCell },
        new() { Key = "seriescount",    Label = "Series",    Width = "15%", Align = "center", Sortable = true, SortKey = "series_count", RenderType = ColumnRenderType.Count,          PropertyName = "SeriesCount" },
        new() { Key = "mediabreakdown", Label = "Media",     Width = "25%", Align = "center", RenderType = ColumnRenderType.MediaBreakdown },
        new() { Key = "peoplecount",    Label = "People",    Width = "15%", Align = "center", Sortable = true, SortKey = "people_count", RenderType = ColumnRenderType.Count,          PropertyName = "PeopleCount" },
    ];

    // ── Hubs tab ──────────────────────────────────────────────────────────────

    /// <summary>Returns the column definitions for the Hubs tab.</summary>
    public static List<VaultColumnDef> GetHubColumns() =>
    [
        Checkbox(),
        new() { Key = "name",      Label = "Name",     Width = "40%", Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.Text,      PropertyName = "Name" },
        new() { Key = "type",      Label = "Type",     Width = "15%", RenderType = ColumnRenderType.Text,      PropertyName = "Type" },
        new() { Key = "itemcount", Label = "Items",    Width = "15%", Align = "center", Sortable = true, SortKey = "item_count", RenderType = ColumnRenderType.Count, PropertyName = "ItemCount" },
        new() { Key = "status",    Label = "Status",   Width = "15%", Sortable = true, SortKey = "status",     RenderType = ColumnRenderType.StatusPill },
        new() { Key = "featured",  Label = "Featured", Width = "10%", Align = "center", RenderType = ColumnRenderType.Text, PropertyName = "Featured" },
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // V2 — Per-tab columns (metadata-focused, no pipeline/status)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Resolves columns by tab ID for the per-media-type tab layout.</summary>
    public static List<VaultColumnDef> GetColumnsByTab(string tabId) =>
        tabId switch
        {
            "new"        => NewTabColumns(),
            "movies"     => MoviesColumnsV2(),
            "tv"         => TvColumnsV2(),
            "music"      => MusicColumnsV2(),
            "books"      => BooksColumnsV2(),
            "audiobooks" => AudiobooksColumnsV2(),
            "podcasts"   => PodcastsColumnsV2(),
            "comics"     => ComicsColumnsV2(),
            "people"     => GetPeopleColumns(),
            "universes"  => GetUniverseColumns(),
            "hubs"       => GetHubColumns(),
            _            => NewTabColumns(),
        };

    /// <summary>Resolves columns by tab ID and view mode.</summary>
    public static List<VaultColumnDef> GetColumnsByTab(string tabId, string viewMode)
    {
        if (tabId == "music" && viewMode == "albums")
            return MusicAlbumContainerColumns();

        return viewMode switch
        {
            "series" or "shows" or "artists" or "albums" => GetContainerColumnsByTab(tabId),
            _ => GetColumnsByTab(tabId), // flat view uses existing V2 columns
        };
    }

    // ── New (recently added) ─────────────────────────────────────────────

    private static List<VaultColumnDef> NewTabColumns() =>
    [
        Checkbox(),
        new() { Key = "media",   Label = "Media Identity", Width = "35%", Sortable = true, SortKey = "title",   RenderType = ColumnRenderType.MediaCell },
        new() { Key = "type",    Label = "Type",           Width = "auto", RenderType = ColumnRenderType.TypeBadge, PropertyName = "MediaType" },
        new() { Key = "added",   Label = "Added",          Width = "auto", Sortable = true, SortKey = "newest",  RenderType = ColumnRenderType.Date, PropertyName = "CreatedAt" },
        new() { Key = "parent",  Label = "Parent Context", Width = "auto", RenderType = ColumnRenderType.Text,   PropertyName = "ParentContext" },
    ];

    // ── Movies V2 ────────────────────────────────────────────────────────

    private static List<VaultColumnDef> MoviesColumnsV2() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Media Identity", Width = "35%",  Sortable = true, SortKey = "title",    RenderType = ColumnRenderType.MediaCell },
        new() { Key = "director", Label = "Director",       Width = "auto", Sortable = true, SortKey = "director", RenderType = ColumnRenderType.Text, PropertyName = "Director" },
        new() { Key = "year",     Label = "Year",           Width = "80px", Sortable = true, SortKey = "year",     RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format",   Label = "Format",         Width = "auto", RenderType = ColumnRenderType.Text,    PropertyName = "ResolutionSummary" },
        new() { Key = "size",     Label = "Size",           Width = "80px", RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
    ];

    // ── TV V2 ────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> TvColumnsV2() =>
    [
        Checkbox(),
        new() { Key = "media",   Label = "Media Identity", Width = "35%",  Sortable = true, SortKey = "title",   RenderType = ColumnRenderType.MediaCell },
        new() { Key = "show",    Label = "Show",           Width = "auto", Sortable = true, SortKey = "show",    RenderType = ColumnRenderType.Text, PropertyName = "ShowName" },
        new() { Key = "season",  Label = "Season",         Width = "80px", Sortable = true, SortKey = "season",  RenderType = ColumnRenderType.Text, PropertyName = "Season",  Align = "center" },
        new() { Key = "episode", Label = "Episode",        Width = "80px", Sortable = true, SortKey = "episode", RenderType = ColumnRenderType.Text, PropertyName = "Episode", Align = "center" },
        new() { Key = "quality", Label = "Quality",        Width = "auto", RenderType = ColumnRenderType.Text,   PropertyName = "ResolutionSummary" },
    ];

    // ── Music V2 ─────────────────────────────────────────────────────────

    private static List<VaultColumnDef> MusicColumnsV2() =>
    [
        Checkbox(),
        new() { Key = "media",   Label = "Media Identity", Width = "30%",  Sortable = true, SortKey = "title",  RenderType = ColumnRenderType.MediaCell },
        new() { Key = "artist",  Label = "Artist",         Width = "auto", Sortable = true, SortKey = "artist", RenderType = ColumnRenderType.Text, PropertyName = "Artist" },
        new() { Key = "album",   Label = "Album",          Width = "20%",  Sortable = true, SortKey = "album",  RenderType = ColumnRenderType.Text, PropertyName = "Album" },
        new() { Key = "bitrate", Label = "Bitrate",        Width = "auto", RenderType = ColumnRenderType.Text,  PropertyName = "ResolutionSummary" },
        new() { Key = "size",    Label = "Size",           Width = "80px", RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
    ];

    // ── Books V2 ─────────────────────────────────────────────────────────

    private static List<VaultColumnDef> BooksColumnsV2() =>
    [
        Checkbox(),
        new() { Key = "media",  Label = "Media Identity", Width = "35%",  Sortable = true, SortKey = "title",  RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author", Label = "Author",         Width = "auto", Sortable = true, SortKey = "author", RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "series", Label = "Series",         Width = "auto", Sortable = true, SortKey = "series", RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",   Label = "Year",           Width = "80px", Sortable = true, SortKey = "year",   RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format", Label = "Format",         Width = "auto", RenderType = ColumnRenderType.Text,  PropertyName = "ResolutionSummary" },
    ];

    // ── Audiobooks V2 ────────────────────────────────────────────────────

    private static List<VaultColumnDef> AudiobooksColumnsV2() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Media Identity", Width = "30%",  Sortable = true, SortKey = "title",    RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author",   Label = "Author",         Width = "auto", Sortable = true, SortKey = "author",   RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "narrator", Label = "Narrator",       Width = "auto", Sortable = true, SortKey = "narrator", RenderType = ColumnRenderType.Text, PropertyName = "Narrator" },
        new() { Key = "duration", Label = "Length",         Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "format",   Label = "Format",         Width = "auto", RenderType = ColumnRenderType.Text,    PropertyName = "ResolutionSummary" },
    ];

    // ── Podcasts V2 ──────────────────────────────────────────────────────

    private static List<VaultColumnDef> PodcastsColumnsV2() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Media Identity", Width = "35%",  Sortable = true, SortKey = "title",   RenderType = ColumnRenderType.MediaCell },
        new() { Key = "show",     Label = "Publisher",      Width = "auto", Sortable = true, SortKey = "podcast", RenderType = ColumnRenderType.Text, PropertyName = "ShowName" },
        new() { Key = "episode",  Label = "Episodes",       Width = "80px", RenderType = ColumnRenderType.Text,   PropertyName = "Episode", Align = "center" },
        new() { Key = "duration", Label = "Duration",       Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "genre",    Label = "Genre",          Width = "auto", RenderType = ColumnRenderType.Text,   PropertyName = "Genre" },
    ];

    // ── Comics V2 ────────────────────────────────────────────────────────

    private static List<VaultColumnDef> ComicsColumnsV2() =>
    [
        Checkbox(),
        new() { Key = "media",  Label = "Media Identity", Width = "35%",  Sortable = true, SortKey = "title",  RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author", Label = "Writer",         Width = "auto", Sortable = true, SortKey = "author", RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "series", Label = "Series",         Width = "auto", Sortable = true, SortKey = "series", RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",   Label = "Year",           Width = "80px", Sortable = true, SortKey = "year",   RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format", Label = "Format",         Width = "auto", RenderType = ColumnRenderType.Text,  PropertyName = "ResolutionSummary" },
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // V3 — Container-level columns (for grouped/container views)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Resolves container-level columns by tab ID.</summary>
    public static List<VaultColumnDef> GetContainerColumnsByTab(string tabId) =>
        tabId switch
        {
            "movies"     => MovieSeriesContainerColumns(),
            "tv"         => TvShowContainerColumns(),
            "music"      => MusicArtistContainerColumns(),
            "books"      => BookSeriesContainerColumns(),
            "audiobooks" => AudiobookSeriesContainerColumns(),
            "podcasts"   => PodcastShowContainerColumns(),
            "comics"     => ComicSeriesContainerColumns(),
            _            => [],
        };

    /// <summary>Resolves container-level columns for album view in Music tab.</summary>
    public static List<VaultColumnDef> MusicAlbumContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Album",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "artist",    Label = "Artist",   Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "tracks",    Label = "Tracks",   Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "year",      Label = "Year",     Width = "80px", Align = "center", RenderType = ColumnRenderType.Text, PropertyName = "Year" },
    ];

    // ── Movie Series containers ──────────────────────────────────────────

    private static List<VaultColumnDef> MovieSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Series",    Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "films",     Label = "Films",      Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "year",      Label = "Year Range", Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Year" },
    ];

    // ── TV Show containers ───────────────────────────────────────────────

    private static List<VaultColumnDef> TvShowContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Show",     Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "seasons",   Label = "Seasons",   Width = "80px", Align = "center", RenderType = ColumnRenderType.Count, PropertyName = "SeasonCount" },
        new() { Key = "episodes",  Label = "Episodes",  Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "year",      Label = "Year",      Width = "80px", Align = "center", RenderType = ColumnRenderType.Text, PropertyName = "Year" },
    ];

    // ── Music Artist containers ──────────────────────────────────────────

    private static List<VaultColumnDef> MusicArtistContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Artist",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "albums",    Label = "Albums",    Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "tracks",    Label = "Tracks",    Width = "80px", Align = "center", RenderType = ColumnRenderType.Count, PropertyName = "TrackCount" },
    ];

    // ── Book Series containers ───────────────────────────────────────────

    private static List<VaultColumnDef> BookSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Series",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "author",    Label = "Author",    Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "books",     Label = "Books",     Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
    ];

    // ── Audiobook Series containers ──────────────────────────────────────

    private static List<VaultColumnDef> AudiobookSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container",  Label = "Series",     Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "author",     Label = "Author",      Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "audiobooks", Label = "Audiobooks",  Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
    ];

    // ── Podcast Show containers ──────────────────────────────────────────

    private static List<VaultColumnDef> PodcastShowContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Podcast",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "episodes",  Label = "Episodes",   Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "genre",     Label = "Genre",      Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Genre" },
    ];

    // ── Comic Series containers ──────────────────────────────────────────

    private static List<VaultColumnDef> ComicSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Series",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "writer",    Label = "Writer",    Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "issues",    Label = "Issues",    Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
    ];
}
