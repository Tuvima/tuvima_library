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
    /// <summary>Edit + Delete action buttons for row management.</summary>
    ManageActions,
    /// <summary>Compact cell: small icon + title only, for dense list views (Apple Music style).</summary>
    CompactCell,
    /// <summary>Clickable text — splits on common delimiters and renders each part as a button invoking OnAuthorClicked.</summary>
    ClickableText,
}

/// <summary>Provides default column definitions per media type and tab.</summary>
public static class VaultColumnDefinitions
{
    private static VaultColumnDef Checkbox() => new()
    {
        Key = "checkbox",
        Label = "",
        Width = "4%",
        Align = "center",
        DefaultVisible = true,
        Sortable = false,
        RenderType = ColumnRenderType.Checkbox,
    };

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

    // ── Action Center tab ────────────────────────────────────────────────────

    /// <summary>Returns the column definitions for the Action Center tab.</summary>
    public static List<VaultColumnDef> GetActionCenterColumns() =>
    [
        Checkbox(),
        new() { Key = "media",  Label = "Title",  Width = "30%",  Sortable = true, SortKey = "title",  RenderType = ColumnRenderType.MediaCell },
        new() { Key = "type",   Label = "Type",   Width = "auto", RenderType = ColumnRenderType.TypeBadge, PropertyName = "MediaType" },
        new() { Key = "issue",  Label = "Issue",  Width = "auto", RenderType = ColumnRenderType.Text,    PropertyName = "ReviewTrigger" },
        new() { Key = "status", Label = "Status", Width = "auto", RenderType = ColumnRenderType.StatusPill },
        new() { Key = "manage", Label = "",       Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // ── Per-tab flat columns (metadata-focused) ──
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Resolves columns by tab ID for the per-media-type tab layout.</summary>
    public static List<VaultColumnDef> GetColumnsByTab(string tabId) =>
        tabId switch
        {
            "new"        => NewTabColumns(),
            "movies"     => MoviesColumns(),
            "tv"         => TvColumns(),
            "music"      => MusicColumns(),
            "books"      => BooksColumns(),
            "audiobooks" => AudiobooksColumns(),
            "comics"     => ComicsColumns(),
            "people"        => GetPeopleColumns(),
            "universes"     => GetUniverseColumns(),
            "hubs"          => GetHubColumns(),
            "action_center" => GetActionCenterColumns(),
            _               => NewTabColumns(),
        };

    /// <summary>Resolves columns by tab ID and view mode.</summary>
    public static List<VaultColumnDef> GetColumnsByTab(string tabId, string viewMode)
    {
        if (tabId == "music" && viewMode == "albums")
            return MusicAlbumContainerColumns();

        return viewMode switch
        {
            "series" or "shows" or "artists" or "albums" => GetContainerColumnsByTab(tabId),
            _ => GetColumnsByTab(tabId), // flat view
        };
    }

    // ── New (recently added) ─────────────────────────────────────────────

    private static List<VaultColumnDef> NewTabColumns() =>
    [
        Checkbox(),
        new() { Key = "media",  Label = "Media Identity", Width = "30%", Sortable = true, SortKey = "title",   RenderType = ColumnRenderType.MediaCell },
        new() { Key = "type",   Label = "Type",           Width = "auto", RenderType = ColumnRenderType.TypeBadge, PropertyName = "MediaType" },
        new() { Key = "added",  Label = "Added",          Width = "auto", Sortable = true, SortKey = "newest",  RenderType = ColumnRenderType.Date, PropertyName = "CreatedAt" },
        new() { Key = "specs",  Label = "Specs",          Width = "auto", Sortable = true, SortKey = "specs",   RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
        new() { Key = "size",   Label = "Size",           Width = "80px", Sortable = true, SortKey = "size",    RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
        new() { Key = "manage", Label = "", Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // ── Movies ───────────────────────────────────────────────────────────

    private static List<VaultColumnDef> MoviesColumns() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Title",           Width = "30%",  Sortable = true, SortKey = "title",    RenderType = ColumnRenderType.MediaCell },
        new() { Key = "director", Label = "Director",       Width = "auto", Sortable = true, SortKey = "director", RenderType = ColumnRenderType.Text, PropertyName = "Director" },
        new() { Key = "series",   Label = "Series",         Width = "auto", Sortable = true, SortKey = "series",   RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",     Label = "Year",           Width = "80px", Sortable = true, SortKey = "year",     RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format",   Label = "Format",         Width = "auto", Sortable = true, SortKey = "specs", RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
        new() { Key = "size",     Label = "Size",           Width = "80px", RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
        new() { Key = "manage", Label = "", Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // ── TV ───────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> TvColumns() =>
    [
        Checkbox(),
        new() { Key = "media",         Label = "Episode",       Width = "30%",  Sortable = true, SortKey = "title",          RenderType = ColumnRenderType.MediaCell },
        new() { Key = "episode_title", Label = "Episode Title", Width = "auto", Sortable = true, SortKey = "episode_title",  RenderType = ColumnRenderType.Text, PropertyName = "EpisodeTitle" },
        new() { Key = "season",        Label = "Season",        Width = "80px", Sortable = true, SortKey = "season",         RenderType = ColumnRenderType.Text, PropertyName = "Season",  Align = "center" },
        new() { Key = "episode",       Label = "Episode",       Width = "80px", Sortable = true, SortKey = "episode",        RenderType = ColumnRenderType.Text, PropertyName = "Episode", Align = "center" },
        new() { Key = "quality",       Label = "Quality",       Width = "auto", Sortable = true, SortKey = "specs",          RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
        new() { Key = "size",          Label = "Size",          Width = "80px", RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
        new() { Key = "manage",        Label = "", Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // ── Music ────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> MusicColumns() =>
    [
        Checkbox(),
        new() { Key = "media",   Label = "Song",   Width = "30%",  Sortable = true, SortKey = "title",  RenderType = ColumnRenderType.MediaCell },
        new() { Key = "artist",  Label = "Artist",  Width = "auto", Sortable = true, SortKey = "artist", RenderType = ColumnRenderType.ClickableText, PropertyName = "Artist" },
        new() { Key = "album",   Label = "Album",   Width = "auto", Sortable = true, SortKey = "album",  RenderType = ColumnRenderType.Text, PropertyName = "Album" },
        new() { Key = "genre",   Label = "Genre",   Width = "auto", RenderType = ColumnRenderType.Text,  PropertyName = "Genre" },
        new() { Key = "size",    Label = "Size",    Width = "80px", RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
        new() { Key = "manage", Label = "", Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // ── Books ────────────────────────────────────────────────────────────

    private static List<VaultColumnDef> BooksColumns() =>
    [
        Checkbox(),
        new() { Key = "media",  Label = "Title",           Width = "30%",  Sortable = true, SortKey = "title",  RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author", Label = "Author",         Width = "auto", Sortable = true, SortKey = "author", RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "series", Label = "Series",         Width = "auto", Sortable = true, SortKey = "series", RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",   Label = "Year",           Width = "80px", Sortable = true, SortKey = "year",   RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format", Label = "Format",         Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
        new() { Key = "manage", Label = "", Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // ── Audiobooks ───────────────────────────────────────────────────────

    private static List<VaultColumnDef> AudiobooksColumns() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Title",           Width = "30%",  Sortable = true, SortKey = "title",    RenderType = ColumnRenderType.MediaCell },
        new() { Key = "author",   Label = "Author",         Width = "auto", Sortable = true, SortKey = "author",   RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "narrator", Label = "Narrator",       Width = "auto", Sortable = true, SortKey = "narrator", RenderType = ColumnRenderType.Text, PropertyName = "Narrator" },
        new() { Key = "series",   Label = "Series",         Width = "auto", Sortable = true, SortKey = "series",   RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",     Label = "Year",           Width = "80px", Sortable = true, SortKey = "year",     RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "duration", Label = "Length",         Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "format",   Label = "Format",         Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
        new() { Key = "manage", Label = "", Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // ── Comics ───────────────────────────────────────────────────────────

    private static List<VaultColumnDef> ComicsColumns() =>
    [
        Checkbox(),
        new() { Key = "media",    Label = "Issue",           Width = "30%",  Sortable = true, SortKey = "title",           RenderType = ColumnRenderType.MediaCell },
        new() { Key = "series",   Label = "Series",         Width = "auto", Sortable = true, SortKey = "series",          RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "issue_no", Label = "#",              Width = "60px", Sortable = true, SortKey = "series_position", RenderType = ColumnRenderType.Text, PropertyName = "SeriesPosition", Align = "center" },
        new() { Key = "author",   Label = "Writer",         Width = "auto", Sortable = true, SortKey = "author",          RenderType = ColumnRenderType.Text, PropertyName = "Author" },
        new() { Key = "year",     Label = "Year",           Width = "80px", Sortable = true, SortKey = "year",            RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format",   Label = "Format",         Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
        new() { Key = "manage",   Label = "", Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // ── Container-level columns (for grouped/container views) ──
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
        new() { Key = "network",   Label = "Network",   Width = "auto", Sortable = true, SortKey = "network",    RenderType = ColumnRenderType.Text, PropertyName = "Network" },
        new() { Key = "seasons",   Label = "Seasons",   Width = "80px", Align = "center", RenderType = ColumnRenderType.Count, PropertyName = "SeasonCount" },
        new() { Key = "episodes",  Label = "Episodes",  Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "year",      Label = "Year",      Width = "80px", Align = "center", RenderType = ColumnRenderType.Text, PropertyName = "Year" },
    ];

    // ── Music Artist containers ──────────────────────────────────────────

    private static List<VaultColumnDef> MusicArtistContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Artist",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "albums",    Label = "Albums",    Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "AlbumCount" },
        new() { Key = "tracks",    Label = "Tracks",    Width = "80px", Align = "center", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
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

    // ── Comic Series containers ──────────────────────────────────────────

    private static List<VaultColumnDef> ComicSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Series",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "writer",    Label = "Writer",    Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "issues",    Label = "Issues",    Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
    ];
}
