namespace MediaEngine.Web.Components.Library;

/// <summary>Defines a single column in the configurable library table.</summary>
public sealed class LibraryColumnDef
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

    /// <summary>Column render type â€” determines which template the table uses.</summary>
    public ColumnRenderType RenderType { get; init; } = ColumnRenderType.Text;

    /// <summary>Property path on LibraryItemViewModel to read value from (for Text/Date columns).</summary>
    public string? PropertyName { get; init; }
}

/// <summary>How the column should be rendered.</summary>
public enum ColumnRenderType
{
    /// <summary>Plain text from PropertyName.</summary>
    Text,
    /// <summary>The media cell: thumbnail + title + subtitle + creator.</summary>
    MediaCell,
    /// <summary>Pipeline stage dots (2 StageGate components: Identified, Enriched).</summary>
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
    /// <summary>Clickable text â€” splits on common delimiters and renders each part as a button invoking OnAuthorClicked.</summary>
    ClickableText,
    /// <summary>Small standalone cover art thumbnail (32x32, no overlay badge).</summary>
    CoverThumb,
    /// <summary>Title text with inline status dot indicator.</summary>
    TitleCell,
}

/// <summary>Provides default column definitions per media type and tab.</summary>
public static class LibraryColumnDefinitions
{
    private static LibraryColumnDef Checkbox() => new()
    {
        Key = "checkbox",
        Label = "",
        Width = "4%",
        Align = "center",
        DefaultVisible = true,
        Sortable = false,
        RenderType = ColumnRenderType.Checkbox,
    };

    private static LibraryColumnDef CoverThumb() => new()
    {
        Key = "cover",
        Label = "",
        Width = "40px",
        Align = "center",
        DefaultVisible = true,
        Sortable = false,
        RenderType = ColumnRenderType.CoverThumb,
    };

    private static LibraryColumnDef TitleColumn(string label = "Title", string sortKey = "title") => new()
    {
        Key = "title",
        Label = label,
        Width = "auto",
        Sortable = true,
        SortKey = sortKey,
        RenderType = ColumnRenderType.TitleCell,
    };

    // â”€â”€ People tab â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Returns the column definitions for the People tab.</summary>
    public static List<LibraryColumnDef> GetPeopleColumns() =>
    [
        Checkbox(),
        new() { Key = "person",   Label = "Name",       Width = "60%", Sortable = true, SortKey = "name",     RenderType = ColumnRenderType.PersonCell },
        new() { Key = "roles",    Label = "Roles",      Width = "20%", RenderType = ColumnRenderType.RoleChips },
        new() { Key = "presence", Label = "In Library", Width = "20%", Align = "right", Sortable = true, SortKey = "presence", RenderType = ColumnRenderType.PresenceCount },
    ];

    // â”€â”€ Universes tab â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Returns the column definitions for the Universes tab.</summary>
    public static List<LibraryColumnDef> GetUniverseColumns() =>
    [
        Checkbox(),
        new() { Key = "universe",       Label = "Universe",  Width = "40%", Sortable = true, SortKey = "name",         RenderType = ColumnRenderType.UniverseCell },
        new() { Key = "seriescount",    Label = "Series",    Width = "15%", Align = "center", Sortable = true, SortKey = "series_count", RenderType = ColumnRenderType.Count,          PropertyName = "SeriesCount" },
        new() { Key = "mediabreakdown", Label = "Media",     Width = "25%", Align = "center", RenderType = ColumnRenderType.MediaBreakdown },
        new() { Key = "peoplecount",    Label = "People",    Width = "15%", Align = "center", Sortable = true, SortKey = "people_count", RenderType = ColumnRenderType.Count,          PropertyName = "PeopleCount" },
    ];

    // â”€â”€ Collections tab â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Returns the column definitions for the Collections tab.</summary>
    public static List<LibraryColumnDef> GetCollectionColumns() =>
    [
        Checkbox(),
        new() { Key = "name",      Label = "Name",     Width = "40%", Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.Text,      PropertyName = "Name" },
        new() { Key = "type",      Label = "Type",     Width = "15%", RenderType = ColumnRenderType.Text,      PropertyName = "Type" },
        new() { Key = "itemcount", Label = "Items",    Width = "15%", Align = "center", Sortable = true, SortKey = "item_count", RenderType = ColumnRenderType.Count, PropertyName = "ItemCount" },
        new() { Key = "status",    Label = "Status",   Width = "15%", Sortable = true, SortKey = "status",     RenderType = ColumnRenderType.StatusPill },
        new() { Key = "featured",  Label = "Featured", Width = "10%", Align = "center", RenderType = ColumnRenderType.Text, PropertyName = "Featured" },
    ];

    // â”€â”€ Action Center tab â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Returns the column definitions for the Action Center tab.</summary>
    public static List<LibraryColumnDef> GetActionCenterColumns() =>
    [
        Checkbox(),
        new() { Key = "media",  Label = "Title",  Width = "30%",  Sortable = true, SortKey = "title",  RenderType = ColumnRenderType.MediaCell },
        new() { Key = "type",   Label = "Type",   Width = "auto", RenderType = ColumnRenderType.TypeBadge, PropertyName = "MediaType" },
        new() { Key = "issue",  Label = "Issue",  Width = "auto", RenderType = ColumnRenderType.Text,    PropertyName = "ReviewTrigger" },
        new() { Key = "status", Label = "Status", Width = "auto", RenderType = ColumnRenderType.StatusPill },
        new() { Key = "manage", Label = "",       Width = "80px", Align = "center", RenderType = ColumnRenderType.ManageActions },
    ];

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // â”€â”€ Per-tab flat columns (metadata-focused) â”€â”€
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>Resolves columns by tab ID for the per-media-type tab layout.</summary>
    public static List<LibraryColumnDef> GetColumnsByTab(string tabId) =>
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
            "collections"          => GetCollectionColumns(),
            "action_center" => GetActionCenterColumns(),
            _               => NewTabColumns(),
        };

    /// <summary>Resolves columns by tab ID and view mode.</summary>
    public static List<LibraryColumnDef> GetColumnsByTab(string tabId, string viewMode)
    {
        if (tabId == "music" && viewMode == "albums")
            return MusicAlbumContainerColumns();

        return viewMode switch
        {
            "series" or "shows" or "artists" or "albums" => GetContainerColumnsByTab(tabId),
            _ => GetColumnsByTab(tabId), // flat view
        };
    }

    // â”€â”€ New (recently added) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> NewTabColumns() =>
    [
        Checkbox(),
        CoverThumb(),
        TitleColumn(),
        new() { Key = "type",    Label = "Type",    Width = "auto", RenderType = ColumnRenderType.TypeBadge, PropertyName = "MediaType" },
        new() { Key = "creator", Label = "Creator", Width = "auto", Sortable = true, SortKey = "author", RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "context", Label = "Context", Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "ContextLabel" },
        new() { Key = "added",   Label = "Added",   Width = "auto", Sortable = true, SortKey = "newest", RenderType = ColumnRenderType.Date, PropertyName = "CreatedAt" },
        new() { Key = "size",    Label = "Size",    Width = "80px",  Sortable = true, SortKey = "size", RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
    ];

    // â”€â”€ Movies â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> MoviesColumns() =>
    [
        Checkbox(),
        CoverThumb(),
        TitleColumn(),
        new() { Key = "director", Label = "Director",   Width = "auto", Sortable = true, SortKey = "director", RenderType = ColumnRenderType.ClickableText, PropertyName = "Director" },
        new() { Key = "series",   Label = "Series",     Width = "auto", Sortable = true, SortKey = "series",   RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",     Label = "Year",       Width = "70px", Sortable = true, SortKey = "year",     RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format",   Label = "Quality",    Width = "auto", Sortable = true, SortKey = "specs",    RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
        new() { Key = "size",     Label = "Size",       Width = "80px", Sortable = true, SortKey = "size",     RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
    ];

    // â”€â”€ TV â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> TvColumns() =>
    [
        Checkbox(),
        CoverThumb(),
        TitleColumn("Episode"),
        new() { Key = "show",     Label = "Show",       Width = "auto", Sortable = true, SortKey = "series",         RenderType = ColumnRenderType.Text, PropertyName = "ShowName" },
        new() { Key = "season",   Label = "Season",     Width = "70px", Sortable = true, SortKey = "season",         RenderType = ColumnRenderType.Text, PropertyName = "Season",  Align = "center" },
        new() { Key = "episode",  Label = "Ep",         Width = "50px", Sortable = true, SortKey = "episode",        RenderType = ColumnRenderType.Text, PropertyName = "Episode", Align = "center" },
        new() { Key = "quality",  Label = "Quality",    Width = "auto", Sortable = true, SortKey = "specs",          RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
        new() { Key = "size",     Label = "Size",       Width = "80px", RenderType = ColumnRenderType.FileSize, PropertyName = "FileSizeBytes", Align = "right" },
    ];

    // â”€â”€ Music â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> MusicColumns() =>
    [
        Checkbox(),
        TitleColumn(),
        new() { Key = "time",         Label = "Time",         Width = "84px",  Align = "right",  DefaultVisible = true,  Sortable = true, SortKey = "time",         RenderType = ColumnRenderType.Text,        PropertyName = "Duration" },
        new() { Key = "artist",       Label = "Artist",       Width = "16%",   DefaultVisible = true,  Sortable = true, SortKey = "artist",       RenderType = ColumnRenderType.ClickableText, PropertyName = "Artist" },
        new() { Key = "album",        Label = "Album",        Width = "18%",   DefaultVisible = true,  Sortable = true, SortKey = "album",        RenderType = ColumnRenderType.Text,        PropertyName = "Album" },
        new() { Key = "genre",        Label = "Genre",        Width = "12%",   DefaultVisible = true,  Sortable = true, SortKey = "genre",        RenderType = ColumnRenderType.Text,        PropertyName = "Genre" },
        new() { Key = "favorite",     Label = "Favorite",     Width = "90px",  Align = "center", DefaultVisible = true,  Sortable = true, SortKey = "favorite",     RenderType = ColumnRenderType.Text,        PropertyName = "Favorite" },
        new() { Key = "plays",        Label = "Plays",        Width = "84px",  Align = "right",  DefaultVisible = true,  Sortable = true, SortKey = "plays",        RenderType = ColumnRenderType.Text,        PropertyName = "PlayCount" },
        new() { Key = "date_added",   Label = "Date Added",   Width = "168px", DefaultVisible = true,  Sortable = true, SortKey = "dateAdded",    RenderType = ColumnRenderType.Text,        PropertyName = "DateAdded" },
        new() { Key = "album_artist", Label = "Album Artist", Width = "16%",   DefaultVisible = false, Sortable = true, SortKey = "album_artist", RenderType = ColumnRenderType.Text,        PropertyName = "AlbumArtist" },
        new() { Key = "composer",     Label = "Composer",     Width = "16%",   DefaultVisible = false, Sortable = true, SortKey = "composer",     RenderType = ColumnRenderType.Text,        PropertyName = "Composer" },
        new() { Key = "comments",     Label = "Comments",     Width = "22%",   DefaultVisible = false, Sortable = true, SortKey = "comments",     RenderType = ColumnRenderType.Text,        PropertyName = "Comments" },
        new() { Key = "date_modified",Label = "Date Modified",Width = "168px", DefaultVisible = false, Sortable = true, SortKey = "date_modified",RenderType = ColumnRenderType.Text,        PropertyName = "DateModified" },
        new() { Key = "disc_number",  Label = "Disc Number",  Width = "104px", Align = "center", DefaultVisible = false, Sortable = true, SortKey = "disc_number",  RenderType = ColumnRenderType.Text,        PropertyName = "DiscNumber" },
        new() { Key = "kind",         Label = "Kind",         Width = "104px", DefaultVisible = false, Sortable = true, SortKey = "kind",         RenderType = ColumnRenderType.Text,        PropertyName = "Kind" },
        new() { Key = "last_played",  Label = "Last Played",  Width = "168px", DefaultVisible = false, Sortable = true, SortKey = "last_played",  RenderType = ColumnRenderType.Text,        PropertyName = "LastPlayed" },
        new() { Key = "rating",       Label = "Rating",       Width = "110px", Align = "center", DefaultVisible = false, Sortable = true, SortKey = "rating",       RenderType = ColumnRenderType.Text,        PropertyName = "Rating" },
        new() { Key = "release_date", Label = "Release Date", Width = "168px", DefaultVisible = false, Sortable = true, SortKey = "release_date", RenderType = ColumnRenderType.Text,        PropertyName = "ReleaseDate" },
        new() { Key = "size",         Label = "Size",         Width = "84px",  Align = "right",  DefaultVisible = false, Sortable = true, SortKey = "size",         RenderType = ColumnRenderType.FileSize,    PropertyName = "FileSizeBytes" },
        new() { Key = "sort_album",   Label = "Sort Album",   Width = "18%",   DefaultVisible = false, Sortable = true, SortKey = "sort_album",   RenderType = ColumnRenderType.Text,        PropertyName = "SortAlbum" },
        new() { Key = "sort_artist",  Label = "Sort Artist",  Width = "18%",   DefaultVisible = false, Sortable = true, SortKey = "sort_artist",  RenderType = ColumnRenderType.Text,        PropertyName = "SortArtist" },
        new() { Key = "sort_title",   Label = "Sort Title",   Width = "18%",   DefaultVisible = false, Sortable = true, SortKey = "sort_title",   RenderType = ColumnRenderType.Text,        PropertyName = "SortTitle" },
        new() { Key = "track_number", Label = "Track Number", Width = "114px", Align = "center", DefaultVisible = false, Sortable = true, SortKey = "track_number", RenderType = ColumnRenderType.Text,        PropertyName = "TrackNumber" },
        new() { Key = "year",         Label = "Year",         Width = "80px",  Align = "center", DefaultVisible = false, Sortable = true, SortKey = "year",         RenderType = ColumnRenderType.Text,        PropertyName = "Year" },
    ];

    // â”€â”€ Books â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> BooksColumns() =>
    [
        Checkbox(),
        CoverThumb(),
        TitleColumn(),
        new() { Key = "author", Label = "Author", Width = "auto", Sortable = true, SortKey = "author", RenderType = ColumnRenderType.ClickableText, PropertyName = "Author" },
        new() { Key = "series", Label = "Series", Width = "auto", Sortable = true, SortKey = "series", RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",   Label = "Year",   Width = "70px", Sortable = true, SortKey = "year",   RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format", Label = "Format", Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
    ];

    // â”€â”€ Audiobooks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> AudiobooksColumns() =>
    [
        Checkbox(),
        CoverThumb(),
        TitleColumn(),
        new() { Key = "author",   Label = "Author",   Width = "auto", Sortable = true, SortKey = "author",   RenderType = ColumnRenderType.ClickableText, PropertyName = "Author" },
        new() { Key = "narrator", Label = "Narrator", Width = "auto", Sortable = true, SortKey = "narrator", RenderType = ColumnRenderType.Text, PropertyName = "Narrator" },
        new() { Key = "series",   Label = "Series",   Width = "auto", Sortable = true, SortKey = "series",   RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "year",     Label = "Year",     Width = "70px", Sortable = true, SortKey = "year",     RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "duration", Label = "Length",   Width = "auto", RenderType = ColumnRenderType.Duration },
        new() { Key = "format",   Label = "Format",   Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
    ];

    // â”€â”€ Comics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> ComicsColumns() =>
    [
        Checkbox(),
        CoverThumb(),
        TitleColumn("Issue"),
        new() { Key = "series",   Label = "Series", Width = "auto", Sortable = true, SortKey = "series",          RenderType = ColumnRenderType.Text, PropertyName = "Series" },
        new() { Key = "issue_no", Label = "#",      Width = "50px", Sortable = true, SortKey = "series_position", RenderType = ColumnRenderType.Text, PropertyName = "SeriesPosition", Align = "center" },
        new() { Key = "author",   Label = "Writer", Width = "auto", Sortable = true, SortKey = "author",          RenderType = ColumnRenderType.ClickableText, PropertyName = "Author" },
        new() { Key = "year",     Label = "Year",   Width = "70px", Sortable = true, SortKey = "year",            RenderType = ColumnRenderType.Text, PropertyName = "Year", Align = "center" },
        new() { Key = "format",   Label = "Format", Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Specs" },
    ];

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // â”€â”€ Container-level columns (for grouped/container views) â”€â”€
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>Resolves container-level columns by tab ID.</summary>
    public static List<LibraryColumnDef> GetContainerColumnsByTab(string tabId) =>
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
    public static List<LibraryColumnDef> MusicAlbumContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Album",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "artist",    Label = "Artist",   Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "tracks",    Label = "Tracks",   Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "year",      Label = "Year",     Width = "80px", Align = "center", RenderType = ColumnRenderType.Text, PropertyName = "Year" },
    ];

    // â”€â”€ Movie Series containers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> MovieSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Series",    Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "films",     Label = "Films",      Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "year",      Label = "Year Range", Width = "auto", RenderType = ColumnRenderType.Text, PropertyName = "Year" },
    ];

    // â”€â”€ TV Show containers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> TvShowContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Show",     Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "network",   Label = "Network",   Width = "auto", Sortable = true, SortKey = "network",    RenderType = ColumnRenderType.Text, PropertyName = "Network" },
        new() { Key = "seasons",   Label = "Seasons",   Width = "80px", Align = "center", RenderType = ColumnRenderType.Count, PropertyName = "SeasonCount" },
        new() { Key = "episodes",  Label = "Episodes",  Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
        new() { Key = "year",      Label = "Year",      Width = "80px", Align = "center", RenderType = ColumnRenderType.Text, PropertyName = "Year" },
    ];

    // â”€â”€ Music Artist containers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> MusicArtistContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Artist",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "albums",    Label = "Albums",    Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "AlbumCount" },
        new() { Key = "tracks",    Label = "Tracks",    Width = "80px", Align = "center", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
    ];

    // â”€â”€ Book Series containers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> BookSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Series",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "author",    Label = "Author",    Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "books",     Label = "Books",     Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
    ];

    // â”€â”€ Audiobook Series containers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> AudiobookSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container",  Label = "Series",     Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "author",     Label = "Author",      Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "audiobooks", Label = "Audiobooks",  Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
    ];

    // â”€â”€ Comic Series containers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static List<LibraryColumnDef> ComicSeriesContainerColumns() =>
    [
        Checkbox(),
        new() { Key = "container", Label = "Series",   Width = "40%",  Sortable = true, SortKey = "name",       RenderType = ColumnRenderType.ContainerCell },
        new() { Key = "writer",    Label = "Writer",    Width = "auto", Sortable = true, SortKey = "creator",    RenderType = ColumnRenderType.Text, PropertyName = "Creator" },
        new() { Key = "issues",    Label = "Issues",    Width = "80px", Align = "center", Sortable = true, SortKey = "work_count", RenderType = ColumnRenderType.Count, PropertyName = "WorkCount" },
    ];
}

