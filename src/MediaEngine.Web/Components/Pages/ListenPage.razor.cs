using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MediaEngine.Web.Components.Library;
using MediaEngine.Web.Components.Listen;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Discovery;
using MediaEngine.Web.Services.Editing;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Navigation;
using MediaEngine.Web.Services.Playback;
using MudBlazor;

namespace MediaEngine.Web.Components.Pages;

public partial class ListenPage
{
    private const string MusicHomeRoute = ListenNavigation.MusicHomeRoute;
    private const string AlbumsRoute = ListenNavigation.AlbumsRoute;
    private const string ArtistsRoute = ListenNavigation.ArtistsRoute;
    private const string SongsRoute = ListenNavigation.SongsRoute;
    private const string PlaylistsRoute = ListenNavigation.PlaylistsRoute;
    private const string AudiobooksRoute = ListenNavigation.AudiobooksRoute;

    [Inject] private IEngineApiClient ApiClient { get; set; } = default!;
    [Inject] private UIOrchestratorService Orchestrator { get; set; } = default!;
    [Inject] private ListenPlaybackService Playback { get; set; } = default!;
    [Inject] private MediaReactionService Reactions { get; set; } = default!;
    [Inject] private MediaEditorLauncherService MediaEditorLauncher { get; set; } = default!;
    [Inject] private CollectionEditorLauncherService CollectionEditorLauncher { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public string? Section { get; set; }
    [Parameter] public Guid? CollectionId { get; set; }
    [Parameter] public string? AlbumKey { get; set; }
    [Parameter] public string? ArtistKey { get; set; }
    [Parameter] public string? PlaylistKey { get; set; }
    [Parameter] public Guid? WorkId { get; set; }
    [SupplyParameterFromQuery(Name = "track")] public Guid? Track { get; set; }
    [SupplyParameterFromQuery(Name = "edit")] public bool Edit { get; set; }
    [SupplyParameterFromQuery(Name = "artist")] public string? AlbumArtist { get; set; }

    private readonly List<WorkViewModel> _allWorks = [];
    private readonly List<WorkViewModel> _musicWorks = [];
    private readonly List<JourneyItemViewModel> _musicJourney = [];
    private readonly List<WorkViewModel> _audiobookWorks = [];
    private readonly List<ContentGroupViewModel> _albumGroups = [];
    private readonly List<ContentGroupViewModel> _artistGroups = [];
    private readonly List<ManagedCollectionViewModel> _managedCollections = [];
    private readonly List<CollectionItemViewModel> _playlistItems = [];
    private readonly Dictionary<Guid, WorkViewModel> _workLookup = [];
    private readonly Dictionary<string, CollectionGroupDetailViewModel?> _artistDetailCache = new(StringComparer.OrdinalIgnoreCase);

    private CollectionGroupDetailViewModel? _albumDetail;
    private CollectionGroupDetailViewModel? _artistDetail;
    private DiscoveryPageViewModel _musicHomePage = new() { Key = "listen-music" };
    private ListenTrackDataGrid? _trackGrid;
    private ProfileViewModel? _activeProfile;
    private Guid? _activeProfileId;
    private HashSet<Guid> _favoriteWorkIds = [];
    private HashSet<Guid> _dislikedWorkIds = [];
    private bool _loading = true;
    private bool _redirecting;
    private bool _artistLoading;
    private bool _uiStateLoaded;
    private string? _error;
    private string _songSortColumn = "dateAdded";
    private bool _songSortDescending = true;
    private string _songSearch = string.Empty;
    private string _songGenreFilter = "all";
    private string _songFavoriteFilter = "all";
    private string _songArtistFilter = "all";
    private string _songAlbumFilter = "all";
    private string _songDateAddedFilter = "all";
    private string _albumSearch = string.Empty;
    private string _albumSort = "recent";
    private string _albumArtistFilter = "all";
    private string _albumGenreFilter = "all";
    private bool _albumFavoriteOnly;
    private bool _albumArtOnly;
    private string _artistSearch = string.Empty;
    private string _artistSort = "name";
    private string? _lastHandledTrackContext;
    private string? _selectedArtistName;
    private string? _restoredArtistName;
    private string? _restoredMode;
    private string? _lastPersistedMode;
    private string? _lastPersistedArtistName;
    private HashSet<Guid> _selectedTrackIds = [];
    private HashSet<Guid> _draggingTrackIds = [];
    private Guid? _trackContextMenuWorkId;
    private string _trackContextMenuSourceLabel = "All Music";
    private double _trackContextMenuX;
    private double _trackContextMenuY;
    private bool _playlistsExpanded = true;
    private bool _playlistEditOpen;
    private bool _playlistSaving;
    private string _playlistEditName = string.Empty;
    private string _playlistEditDescription = string.Empty;
    private IBrowserFile? _playlistArtworkFile;
    private string? _playlistArtworkPreviewUrl;
    private string? _playlistNavigationConfig;
    private List<Guid> _playlistOrder = [];
    private Guid? _draggingPlaylistId;
    private bool _playlistCreateMenuOpen;

    private string CurrentPath => Nav.ToAbsoluteUri(Nav.Uri).AbsolutePath;
    private string NormalizedSection => string.IsNullOrWhiteSpace(Section) ? string.Empty : Section.Trim().ToLowerInvariant();
    private bool IsDefaultEntry => string.Equals(CurrentPath, "/listen", StringComparison.OrdinalIgnoreCase);
    private bool IsAudiobooksView => string.Equals(CurrentPath, AudiobooksRoute, StringComparison.OrdinalIgnoreCase);
    private bool IsMusicMode => !IsAudiobooksView;
    private bool IsMusicHome => IsMusicMode
        && (string.Equals(CurrentPath, MusicHomeRoute, StringComparison.OrdinalIgnoreCase)
            || IsDefaultEntry);
    private bool IsAlbumsView => IsMusicMode && !CollectionId.HasValue && string.Equals(NormalizedSection, "albums", StringComparison.OrdinalIgnoreCase);
    private bool IsAlbumDetail => IsMusicMode
        && (CollectionId.HasValue || !string.IsNullOrWhiteSpace(AlbumKey))
        && CurrentPath.StartsWith("/listen/music/albums/", StringComparison.OrdinalIgnoreCase);
    private bool IsSongsView => IsMusicMode && string.Equals(NormalizedSection, "songs", StringComparison.OrdinalIgnoreCase);
    private bool IsPlaylistsView => IsMusicMode && !CollectionId.HasValue && string.IsNullOrWhiteSpace(PlaylistKey) && string.Equals(NormalizedSection, "playlists", StringComparison.OrdinalIgnoreCase);
    private bool IsPlaylistSurface => IsMusicMode && ((CollectionId.HasValue && CurrentPath.StartsWith("/listen/music/playlists/", StringComparison.OrdinalIgnoreCase)) || !string.IsNullOrWhiteSpace(PlaylistKey));
    private bool IsArtistsSurface => IsMusicMode && (string.Equals(NormalizedSection, "artists", StringComparison.OrdinalIgnoreCase) || CurrentPath.StartsWith("/listen/music/artists/", StringComparison.OrdinalIgnoreCase));
    private string SelectedArtistName => _selectedArtistName ?? string.Empty;

    private string PageTitleText => IsAlbumDetail && _albumDetail is not null
        ? $"{_albumDetail.DisplayName} - Listen"
        : IsPlaylistSurface
            ? $"{ActivePlaylistTitle} - Listen"
            : IsArtistsSurface && !string.IsNullOrWhiteSpace(SelectedArtistName)
                ? $"{SelectedArtistName} - Listen"
                : IsMusicHome || IsDefaultEntry
                    ? "Music - Listen"
                : IsAudiobooksView
                    ? "Audiobooks - Listen"
                    : "Listen - Tuvima";

    private IReadOnlyList<WorkViewModel> SortedSongs => SortSongs(_musicWorks);
    private IReadOnlyList<WorkViewModel> FilteredSongs => ApplySongFilters(SortedSongs);
    private IReadOnlyList<WorkViewModel> RecentlyAddedTracks => _musicWorks.OrderByDescending(work => work.CreatedAt).ToList();
    private IReadOnlyList<WorkViewModel> AlbumTracks => _albumDetail is null
        ? []
        : ResolveGroupWorks(_albumDetail.Works)
            .OrderBy(work => ParseTrackNumber(work.TrackNumber))
            .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    private IReadOnlyList<CollectionGroupSeasonViewModel> ArtistAlbums => _artistDetail?.Seasons ?? [];
    private IReadOnlyList<WorkViewModel> ArtistTracks => ResolveArtistTracks();
    private IReadOnlyList<WorkViewModel> ActivePlaylistTracks => ResolveActivePlaylistTracks();
    private IReadOnlyList<ContentGroupViewModel> FilteredAlbumGroups => ApplyAlbumFilters();
    private IReadOnlyList<ContentGroupViewModel> FilteredArtistGroups => ApplyArtistFilters();
    private IReadOnlyList<string> PlaylistCoverUrls => ActivePlaylistTracks.Select(track => track.CoverUrl).Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().Cast<string>().Take(4).ToList();
    private IReadOnlyList<ManagedCollectionViewModel> PlaylistCollections => _managedCollections
        .Where(IsUserVisiblePlaylist)
        .OrderBy(GetPlaylistOrderIndex)
        .ThenBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    private IReadOnlyList<ManagedCollectionViewModel> ManualPlaylistCollections => PlaylistCollections
        .Where(CanAcceptPlaylistItems)
        .ToList();
    private bool HasPlaylistCollections => PlaylistCollections.Count > 0;
    private IReadOnlyList<ListenPlaylistCard> HomePlaylistCards => PlaylistCollections
        .Take(6)
        .Select(ToPlaylistCard)
        .ToList();
    private IReadOnlyList<string> SongGenres => _musicWorks
        .SelectMany(work => work.Genres)
        .Where(genre => !string.IsNullOrWhiteSpace(genre))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(genre => genre, StringComparer.OrdinalIgnoreCase)
        .ToList();
    private IReadOnlyList<string> SongArtists => _musicWorks
        .Select(work => FirstNonBlank(work.Artist, work.Author, null))
        .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, "-", StringComparison.Ordinal))
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    private IReadOnlyList<string> SongAlbums => _musicWorks
        .Select(work => work.Album)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    private IReadOnlyList<string> AlbumArtists => _albumGroups
        .Select(album => album.Creator)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    private bool HasTrackSelection => _selectedTrackIds.Count > 0;
    private int SelectedTrackCount => _selectedTrackIds.Count;
    private string TrackSelectionStatusText => HasTrackSelection
        ? $"{SelectedTrackCount} selected"
        : "Single-click to select, ctrl/shift-click to add more, and use the play icon to start playback.";

    private ManagedCollectionViewModel? ActivePlaylistCollection => CollectionId.HasValue
        ? PlaylistCollections.FirstOrDefault(collection => collection.Id == CollectionId.Value)
        : null;

    private string ActivePlaylistTitle => !string.IsNullOrWhiteSpace(PlaylistKey)
        ? PlaylistKey.ToLowerInvariant() switch
        {
            "all-music" => "All Music",
            "favorite-songs" => "Favorite Songs",
            "recently-added" => "Recently Added",
            _ => "Playlist",
        }
        : ActivePlaylistCollection?.Name ?? "Playlist";

    private string ActivePlaylistTypeLabel => !string.IsNullOrWhiteSpace(PlaylistKey)
        ? "System Playlist"
        : ActivePlaylistCollection?.TypeLabel is { Length: > 0 } label
            ? $"{label} Playlist"
            : "Playlist";

    private string? ActivePlaylistDescription => !string.IsNullOrWhiteSpace(PlaylistKey)
        ? PlaylistKey.ToLowerInvariant() switch
        {
            "all-music" => "Every song in your library in one utility view.",
            "favorite-songs" => "Profile-specific positive reactions rendered as a smart playlist.",
            "recently-added" => "Fresh music sorted by arrival time.",
            _ => null,
        }
        : ActivePlaylistCollection?.Description;

    private string? ActivePlaylistMeta => !string.IsNullOrWhiteSpace(PlaylistKey)
        ? null
        : ActivePlaylistCollection is null
            ? null
            : $"{ActivePlaylistCollection.ItemCount} items";

    private static readonly LibraryBrowsePreset AudiobookBrowsePreset = new()
    {
        RouteBase = "/listen/audiobooks",
        Title = "Audiobooks",
        HeroVariant = BrowseHeroVariant.Listen,
        Tabs =
        [
            new BrowseTabPreset
            {
                Id = "audiobooks",
                Label = "Audiobooks",
                MediaType = "Audiobooks",
                GroupingOptions =
                [
                    new("all", "All Audiobooks", Icons.Material.Outlined.Headphones),
                    new("series", "Series", Icons.Material.Outlined.CollectionsBookmark),
                ],
                DefaultGrouping = "all",
                DefaultLayout = LibraryLayoutMode.Card,
            },
        ],
    };

    private IReadOnlyList<ListenNavItem> QuickAccessItems =>
    [
        new("Recently Added", "/listen/music/playlists/system/recently-added", Icons.Material.Outlined.Schedule, RecentlyAddedTracks.Count.ToString(CultureInfo.InvariantCulture)),
        new("Favorite Songs", "/listen/music/playlists/system/favorite-songs", Icons.Material.Outlined.FavoriteBorder, _favoriteWorkIds.Count.ToString(CultureInfo.InvariantCulture)),
    ];

    private IReadOnlyList<ListenNavItem> MusicLibraryItems
    {
        get
        {
            var items = new List<ListenNavItem>
            {
                new("Home", MusicHomeRoute, Icons.Material.Outlined.Home, null),
                new("Albums", AlbumsRoute, Icons.Material.Outlined.Album, _albumGroups.Count.ToString(CultureInfo.InvariantCulture)),
                new("Artists", ArtistsRoute, Icons.Material.Outlined.PersonOutline, _artistGroups.Count.ToString(CultureInfo.InvariantCulture)),
                new("Songs", SongsRoute, Icons.Material.Outlined.MusicNote, _musicWorks.Count.ToString(CultureInfo.InvariantCulture)),
            };

            return items;
        }
    }

    private IReadOnlyList<ListenNavItem> AudiobookLibraryItems =>
    [
        new("All Audiobooks", AudiobooksRoute, Icons.Material.Outlined.Headphones, _audiobookWorks.Count.ToString(CultureInfo.InvariantCulture)),
    ];

    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_uiStateLoaded)
        {
            await RestoreUiStateAsync();
            if (IsDefaultEntry)
            {
                return;
            }
        }

        if (_uiStateLoaded && !_loading && !_redirecting && !IsDefaultEntry)
        {
            await PersistUiStateAsync();
        }
    }

    private IReadOnlyList<ListenNavItem> PlaylistNavItems
    {
        get
        {
            var items = new List<ListenNavItem>
            {
                new("All Playlists", PlaylistsRoute, Icons.Material.Outlined.QueueMusic),
            };

            items.AddRange(PlaylistCollections.Select(collection =>
                new ListenNavItem(
                    collection.Name,
                    $"/listen/music/playlists/{collection.Id}",
                    PlaylistIconFor(collection),
                    collection.ItemCount.ToString(CultureInfo.InvariantCulture),
                    true)));

            return items;
        }
    }

    private async Task LoadAsync()
    {
        _error = null;
        _loading = true;
        _redirecting = false;
        _albumDetail = null;
        _artistDetail = null;
        _artistLoading = false;
        _playlistItems.Clear();
        _selectedTrackIds.Clear();
        CloseTrackContextMenu();
        _draggingTrackIds.Clear();
        StateHasChanged();

        if (WorkId.HasValue)
        {
            _redirecting = true;
            Nav.NavigateTo($"/book/{WorkId.Value}?mode=listen", replace: true);
            return;
        }

        try
        {
            var profile = await Orchestrator.GetActiveProfileAsync();
            _activeProfile = profile;
            _activeProfileId = profile?.Id;
            _playlistNavigationConfig = profile?.NavigationConfig;
            _playlistOrder = ReadPlaylistOrder(profile?.NavigationConfig);

            var worksTask = Orchestrator.GetLibraryWorksAsync();
            var journeyTask = Orchestrator.GetJourneyAsync(_activeProfileId, 48);
            var albumGroupsTask = ApiClient.GetSystemViewGroupsAsync(mediaType: "Music", groupField: "album");
            var artistGroupsTask = ApiClient.GetSystemViewGroupsAsync(mediaType: "Music", groupField: "artist");
            var musicHomeTask = ApiClient.GetDisplayBrowseAsync(
                lane: "listen",
                mediaType: "Music",
                grouping: "home",
                includeCatalog: false,
                profileId: _activeProfileId);
            var collectionsTask = _activeProfileId.HasValue
                ? ApiClient.GetManagedCollectionsAsync(_activeProfileId.Value)
                : Task.FromResult(new List<ManagedCollectionViewModel>());

            await Task.WhenAll(worksTask, journeyTask, albumGroupsTask, artistGroupsTask, musicHomeTask, collectionsTask);

            _allWorks.Clear();
            _allWorks.AddRange(worksTask.Result);

            _musicWorks.Clear();
            _musicWorks.AddRange(_allWorks.Where(IsMusicWork).OrderBy(work => work.Artist ?? work.Author).ThenBy(work => work.Album).ThenBy(work => ParseTrackNumber(work.TrackNumber)).ThenBy(work => work.Title));

            _audiobookWorks.Clear();
            _audiobookWorks.AddRange(_allWorks.Where(IsAudiobookWork).OrderBy(work => work.Author).ThenBy(work => work.Series).ThenBy(work => work.Title));

            _musicJourney.Clear();
            _musicJourney.AddRange(journeyTask.Result.Where(item => IsMusicWork(item.MediaType)));

            _workLookup.Clear();
            foreach (var work in _allWorks.GroupBy(work => work.Id).Select(group => group.First()))
            {
                _workLookup[work.Id] = work;
            }

            _albumGroups.Clear();
            _albumGroups.AddRange(albumGroupsTask.Result.OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase));

            _artistGroups.Clear();
            _artistGroups.AddRange(artistGroupsTask.Result.OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase));

            _managedCollections.Clear();
            _managedCollections.AddRange(collectionsTask.Result);

            _favoriteWorkIds = (await Reactions.GetFavoriteWorkIdsAsync(_activeProfileId)).ToHashSet();
            _dislikedWorkIds = (await Reactions.GetDislikedWorkIdsAsync(_activeProfileId)).ToHashSet();
            _musicHomePage = DiscoveryComposerService.FromDisplayPage(
                musicHomeTask.Result ?? throw new InvalidOperationException("Display API did not return the music home page."));

            if (IsAlbumDetail)
            {
                _albumDetail = await LoadAlbumDetailAsync();
            }

            if (CollectionId.HasValue && IsPlaylistSurface)
            {
                _playlistItems.AddRange(await ApiClient.GetCollectionItemsAsync(CollectionId.Value, 1000, _activeProfileId));
            }

            _loading = false;

            if (IsArtistsSurface)
            {
                await EnsureArtistSelectionAsync();
            }

            await HandleTrackQueryAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _loading = false;
        }
    }

    private async Task RestoreUiStateAsync()
    {
        _uiStateLoaded = true;

        try
        {
            _restoredMode = await JS.InvokeAsync<string?>("listenUi.getMode");
            _restoredArtistName = await JS.InvokeAsync<string?>("listenUi.getSelectedArtist");
        }
        catch
        {
        }

        _lastPersistedMode = _restoredMode;
        _lastPersistedArtistName = _restoredArtistName;

        if (IsDefaultEntry)
        {
            Nav.NavigateTo(ResolveConfiguredEntryRoute(), replace: true);
            return;
        }

        if (IsArtistsSurface && !_loading && string.IsNullOrWhiteSpace(ArtistKey) && !string.IsNullOrWhiteSpace(_restoredArtistName))
        {
            await SelectArtistAsync(_restoredArtistName, persistSelection: false);
        }
    }

    private async Task PersistUiStateAsync()
    {
        try
        {
            var mode = IsAudiobooksView ? "audiobooks" : "music";
            if (!string.Equals(_lastPersistedMode, mode, StringComparison.OrdinalIgnoreCase))
            {
                await JS.InvokeVoidAsync("listenUi.setMode", mode);
                _lastPersistedMode = mode;
            }

            if (!string.IsNullOrWhiteSpace(_selectedArtistName)
                && !string.Equals(_lastPersistedArtistName, _selectedArtistName, StringComparison.OrdinalIgnoreCase))
            {
                await JS.InvokeVoidAsync("listenUi.setSelectedArtist", _selectedArtistName);
                _lastPersistedArtistName = _selectedArtistName;
                _restoredArtistName = _selectedArtistName;
            }
        }
        catch
        {
        }
    }

    private int GetPlaylistOrderIndex(ManagedCollectionViewModel playlist)
    {
        var index = _playlistOrder.IndexOf(playlist.Id);
        return index < 0 ? int.MaxValue : index;
    }

    private async Task MovePlaylistAsync(Guid playlistId, int direction)
    {
        var orderedIds = PlaylistCollections.Select(playlist => playlist.Id).ToList();
        var index = orderedIds.IndexOf(playlistId);
        if (index < 0)
            return;

        var targetIndex = Math.Clamp(index + direction, 0, orderedIds.Count - 1);
        if (targetIndex == index)
            return;

        orderedIds.RemoveAt(index);
        orderedIds.Insert(targetIndex, playlistId);
        _playlistOrder = orderedIds;
        await SavePlaylistOrderAsync();
    }

    private void BeginPlaylistReorder(Guid playlistId)
    {
        _draggingPlaylistId = playlistId;
    }

    private void EndPlaylistReorder()
    {
        _draggingPlaylistId = null;
    }

    private async Task DropPlaylistReorderAsync(Guid targetPlaylistId)
    {
        if (!_draggingPlaylistId.HasValue || _draggingPlaylistId.Value == targetPlaylistId)
            return;

        var orderedIds = PlaylistCollections.Select(playlist => playlist.Id).ToList();
        var sourceIndex = orderedIds.IndexOf(_draggingPlaylistId.Value);
        var targetIndex = orderedIds.IndexOf(targetPlaylistId);
        if (sourceIndex < 0 || targetIndex < 0)
            return;

        var movedId = orderedIds[sourceIndex];
        orderedIds.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
            targetIndex--;

        orderedIds.Insert(targetIndex, movedId);
        _playlistOrder = orderedIds;
        _draggingPlaylistId = null;
        await SavePlaylistOrderAsync();
    }

    private void TogglePlaylistCreateMenu()
    {
        _playlistCreateMenuOpen = !_playlistCreateMenuOpen;
    }

    private async Task OpenPlaylistCreateDialog(string kind)
    {
        _playlistCreateMenuOpen = false;

        if (_activeProfileId is null)
        {
            Snackbar.Add("An active profile is required to create playlists.", Severity.Warning);
            return;
        }

        var isSmart = string.Equals(kind, "Smart", StringComparison.OrdinalIgnoreCase);
        var changed = await CollectionEditorLauncher.OpenAsync(new CollectionEditorLaunchRequest
        {
            ActiveProfileId = _activeProfileId,
            CanManageSharedCollections = _activeProfile?.Role is "Administrator" or "Curator",
            Mode = isSmart ? "SmartPlaylist" : "Playlist",
            InitialCollectionType = isSmart ? "Smart" : "Playlist",
            InitialRulesEnabled = isSmart,
            InitialTitle = string.Empty,
        });

        if (changed)
            await LoadAsync();
    }
    private async Task SavePlaylistOrderAsync()
    {
        if (_activeProfile is null)
            return;

        var root = ParseNavigationConfig(_playlistNavigationConfig);
        root["listenPlaylistOrder"] = new JsonArray(_playlistOrder.Select(id => JsonValue.Create(id.ToString("D"))).ToArray<JsonNode?>());
        var updatedConfig = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        var saved = await Orchestrator.UpdateProfileAsync(
            _activeProfile.Id,
            _activeProfile.DisplayName,
            _activeProfile.AvatarColor,
            _activeProfile.Role,
            updatedConfig);

        if (saved)
        {
            _playlistNavigationConfig = updatedConfig;
            Snackbar.Add("Playlist order saved.", Severity.Success);
        }
        else
        {
            Snackbar.Add("Playlist order could not be saved.", Severity.Warning);
        }
    }

    private static List<Guid> ReadPlaylistOrder(string? navigationConfig)
    {
        try
        {
            var root = JsonNode.Parse(navigationConfig ?? "{}") as JsonObject;
            var order = root?["listenPlaylistOrder"] as JsonArray;
            return order?
                .Select(node => Guid.TryParse(node?.GetValue<string>(), out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static JsonObject ParseNavigationConfig(string? navigationConfig)
    {
        try
        {
            return JsonNode.Parse(navigationConfig ?? "{}") as JsonObject ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void OpenPlaylistEditor()
    {
        if (ActivePlaylistCollection is null)
            return;

        _playlistEditName = ActivePlaylistCollection.Name;
        _playlistEditDescription = ActivePlaylistCollection.Description ?? string.Empty;
        _playlistArtworkFile = null;
        _playlistArtworkPreviewUrl = null;
        _playlistEditOpen = true;
    }

    private void ClosePlaylistEditor()
    {
        _playlistEditOpen = false;
        _playlistArtworkFile = null;
        _playlistArtworkPreviewUrl = null;
    }

    private async Task OnPlaylistArtworkSelected(InputFileChangeEventArgs args)
    {
        var file = args.File;
        _playlistArtworkFile = file;
        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            _playlistArtworkPreviewUrl = $"data:{(file.ContentType ?? "image/jpeg")};base64,{Convert.ToBase64String(ms.ToArray())}";
        }
        catch
        {
            _playlistArtworkPreviewUrl = null;
            Snackbar.Add("Artwork preview could not be loaded.", Severity.Warning);
        }
    }

    private async Task SavePlaylistEditorAsync()
    {
        if (ActivePlaylistCollection is null)
            return;

        _playlistSaving = true;
        try
        {
            var updated = await ApiClient.UpdateCollectionAsync(
                ActivePlaylistCollection.Id,
                string.IsNullOrWhiteSpace(_playlistEditName) ? ActivePlaylistCollection.Name : _playlistEditName.Trim(),
                _playlistEditDescription.Trim(),
                ActivePlaylistCollection.IconName,
                rules: null,
                matchMode: null,
                visibility: ActivePlaylistCollection.Visibility,
                sortField: null,
                sortDirection: null,
                liveUpdating: null,
                isEnabled: null,
                isFeatured: null,
                profileId: _activeProfileId);

            if (!updated)
            {
                Snackbar.Add("Playlist details could not be saved.", Severity.Warning);
                return;
            }

            if (_playlistArtworkFile is not null)
            {
                await using var stream = _playlistArtworkFile.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
                var uploaded = await ApiClient.UploadCollectionSquareArtworkAsync(
                    ActivePlaylistCollection.Id,
                    stream,
                    _playlistArtworkFile.Name,
                    _activeProfileId);

                if (!uploaded)
                {
                    Snackbar.Add("Playlist artwork could not be uploaded.", Severity.Warning);
                    return;
                }
            }

            Snackbar.Add("Playlist updated.", Severity.Success);
            ClosePlaylistEditor();
            await LoadAsync();
        }
        finally
        {
            _playlistSaving = false;
        }
    }

    private async Task ClearPlaylistArtworkAsync()
    {
        if (ActivePlaylistCollection is null)
            return;

        var cleared = await ApiClient.DeleteCollectionSquareArtworkAsync(ActivePlaylistCollection.Id, _activeProfileId);
        if (cleared)
        {
            Snackbar.Add("Playlist artwork cleared.", Severity.Success);
            ClosePlaylistEditor();
            await LoadAsync();
        }
        else
        {
            Snackbar.Add("Playlist artwork could not be cleared.", Severity.Warning);
        }
    }

    private async Task ReloadAsync() => await LoadAsync();

    private async Task EnsureArtistSelectionAsync()
    {
        if (_artistGroups.Count == 0)
        {
            _selectedArtistName = null;
            _artistDetail = null;
            return;
        }

        var desiredArtist = ResolveDesiredArtistName();
        if (string.IsNullOrWhiteSpace(desiredArtist))
        {
            _selectedArtistName = null;
            _artistDetail = null;
            return;
        }

        await SelectArtistAsync(desiredArtist, persistSelection: false);
    }

    private string? ResolveDesiredArtistName()
    {
        if (!string.IsNullOrWhiteSpace(ArtistKey))
        {
            return CanonicalArtistName(Uri.UnescapeDataString(ArtistKey));
        }

        if (!string.IsNullOrWhiteSpace(_selectedArtistName) && ArtistExists(_selectedArtistName))
        {
            return CanonicalArtistName(_selectedArtistName);
        }

        if (!string.IsNullOrWhiteSpace(_restoredArtistName) && ArtistExists(_restoredArtistName))
        {
            return CanonicalArtistName(_restoredArtistName);
        }

        return _artistGroups.FirstOrDefault()?.DisplayName;
    }

    private bool ArtistExists(string artistName)
        => _artistGroups.Any(artist => string.Equals(artist.DisplayName, artistName, StringComparison.OrdinalIgnoreCase));

    private string CanonicalArtistName(string artistName)
        => _artistGroups.FirstOrDefault(artist => string.Equals(artist.DisplayName, artistName, StringComparison.OrdinalIgnoreCase))?.DisplayName
           ?? artistName;

    private async Task SelectArtistAsync(string artistName, bool persistSelection = true)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return;
        }

        artistName = CanonicalArtistName(artistName);
        if (string.Equals(_selectedArtistName, artistName, StringComparison.OrdinalIgnoreCase) && _artistDetail is not null)
        {
            if (persistSelection)
            {
                await PersistArtistSelectionAsync(artistName);
            }

            return;
        }

        _selectedArtistName = artistName;
        _artistLoading = true;
        _artistDetail = null;
        StateHasChanged();

        try
        {
            if (!_artistDetailCache.TryGetValue(artistName, out var detail))
            {
                detail = await ApiClient.GetArtistDetailByNameAsync(artistName);
                _artistDetailCache[artistName] = detail;
            }

            if (string.Equals(_selectedArtistName, artistName, StringComparison.OrdinalIgnoreCase))
            {
                _artistDetail = detail;
            }
        }
        catch (Exception ex)
        {
            if (string.Equals(_selectedArtistName, artistName, StringComparison.OrdinalIgnoreCase))
            {
                Snackbar.Add($"Could not load {artistName}: {ex.Message}", Severity.Error);
            }
        }
        finally
        {
            if (string.Equals(_selectedArtistName, artistName, StringComparison.OrdinalIgnoreCase))
            {
                _artistLoading = false;

                if (persistSelection)
                {
                    await PersistArtistSelectionAsync(artistName);
                }

                StateHasChanged();
            }
        }
    }

    private async Task PersistArtistSelectionAsync(string artistName)
    {
        if (!_uiStateLoaded)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("listenUi.setSelectedArtist", artistName);
            _lastPersistedArtistName = artistName;
            _restoredArtistName = artistName;
        }
        catch
        {
        }
    }

    private async Task HandleTrackQueryAsync()
    {
        if (!Track.HasValue)
        {
            _lastHandledTrackContext = null;
            return;
        }

        var context = $"{CurrentPath}|{Track.Value}|{Edit}";
        if (string.Equals(_lastHandledTrackContext, context, StringComparison.Ordinal))
        {
            return;
        }

        _lastHandledTrackContext = context;

        if (!_workLookup.TryGetValue(Track.Value, out var requestedTrack))
        {
            return;
        }

        _selectedTrackIds = [requestedTrack.Id];

        if (Edit)
        {
            await OpenTrackEditorAsync(requestedTrack, navigateBackToTrackContext: true);
            return;
        }
    }

    private void NavigateTo(string route)
    {
        CloseTrackContextMenu();
        Nav.NavigateTo(route);
    }

    private async Task<CollectionGroupDetailViewModel?> LoadAlbumDetailAsync()
    {
        if (!string.IsNullOrWhiteSpace(AlbumKey))
        {
            var albumName = Uri.UnescapeDataString(AlbumKey);
            return await ApiClient.GetSystemViewGroupDetailAsync(
                "album",
                albumName,
                mediaType: "Music",
                artistName: string.IsNullOrWhiteSpace(AlbumArtist) ? null : Uri.UnescapeDataString(AlbumArtist));
        }

        if (!CollectionId.HasValue)
        {
            return null;
        }

        var selectedAlbum = _albumGroups.FirstOrDefault(group => group.CollectionId == CollectionId.Value);
        if (selectedAlbum is not null)
        {
            return await ApiClient.GetSystemViewGroupDetailAsync(
                "album",
                selectedAlbum.DisplayName,
                mediaType: "Music",
                artistName: selectedAlbum.Creator);
        }

        return await ApiClient.GetCollectionGroupDetailAsync(CollectionId.Value);
    }

    private void OpenAlbum(ContentGroupViewModel album)
    {
        var route = BuildAlbumDetailRoute(album.DisplayName, album.Creator);
        NavigateTo(route);
    }

    private string BuildAlbumDetailRoute(string? albumName, string? artistName = null, Guid? trackId = null, bool edit = false)
    {
        if (string.IsNullOrWhiteSpace(albumName))
        {
            return AlbumsRoute;
        }

        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(artistName))
        {
            queryParts.Add($"artist={Uri.EscapeDataString(artistName)}");
        }

        if (trackId.HasValue)
        {
            queryParts.Add($"track={trackId.Value:D}");
        }

        if (edit)
        {
            queryParts.Add("edit=true");
        }

        var query = queryParts.Count == 0 ? string.Empty : "?" + string.Join("&", queryParts);
        return $"/listen/music/albums/by-name/{Uri.EscapeDataString(albumName)}{query}";
    }

    private async Task OpenMusicModeAsync()
    {
        NavigateTo(MusicHomeRoute);
        await PersistModePreferenceAsync("music");
    }

    private async Task OpenAudiobooksModeAsync()
    {
        NavigateTo(AudiobooksRoute);
        await PersistModePreferenceAsync("audiobooks");
    }

    private string ResolveConfiguredEntryRoute()
        => ListenNavigation.ResolveEntryRoute(_lastPersistedMode ?? _restoredMode, null);

    private async Task PersistModePreferenceAsync(string mode)
    {
        if (!_uiStateLoaded)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("listenUi.setMode", mode);
            _lastPersistedMode = mode;
        }
        catch
        {
        }
    }

    private bool IsRouteActive(string route)
        => (IsDefaultEntry && string.Equals(route, MusicHomeRoute, StringComparison.OrdinalIgnoreCase))
           || string.Equals(CurrentPath, route, StringComparison.OrdinalIgnoreCase)
           || CurrentPath.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase);

    private async Task PlaySingleWorkAsync(WorkViewModel work, string sourceLabel)
    {
        await Playback.PlayWorkAsync(work, sourceLabel);
        Snackbar.Add($"{work.Title} added to the queue", Severity.Success);
    }

    private async Task PlayTracksAsync(IReadOnlyList<WorkViewModel> works, Guid? startWorkId, string? sourceLabel, bool shuffle = false)
    {
        if (works.Count == 0)
        {
            return;
        }

        var startIndex = 0;
        if (startWorkId.HasValue)
        {
            for (var index = 0; index < works.Count; index++)
            {
                if (works[index].Id == startWorkId.Value)
                {
                    startIndex = index;
                    break;
                }
            }
        }

        await Playback.ReplaceQueueAsync(works, startIndex, sourceLabel, shuffle);
    }

    private async Task QueueTrackAsync(WorkViewModel work)
    {
        await Playback.AddToQueueAsync(work);
        Snackbar.Add($"{work.Title} added to the queue", Severity.Success);
    }

    private async Task EditTrackAsync(WorkViewModel work)
    {
        var albumRoute = BuildAlbumRouteForTrack(work, edit: true);
        if (!string.IsNullOrWhiteSpace(albumRoute))
        {
            NavigateTo(albumRoute);
            return;
        }

        await OpenTrackEditorAsync(work, navigateBackToTrackContext: false);
    }

    private async Task OpenTrackEditorAsync(WorkViewModel work, bool navigateBackToTrackContext)
    {
        var applied = await MediaEditorLauncher.OpenAsync(new MediaEditorLaunchRequest
        {
            EntityIds = [work.Id],
            LaunchEntityId = work.Id,
            LaunchEntityKind = "Work",
            Mode = SharedMediaEditorMode.Normal,
            MediaType = work.MediaType,
            HeaderTitle = work.Title,
            HeaderSubtitle = FirstNonBlank(work.Artist, work.Author, work.Album, work.Series),
            CoverUrl = work.CoverUrl,
            PreviewItems =
            [
                new MediaEditorPreviewItem
                {
                    EntityId = work.Id,
                    Title = work.Title,
                    CoverUrl = work.CoverUrl,
                    MediaType = work.MediaType,
                }
            ],
        });

        if (applied)
        {
            await LoadAsync();
        }

        var trackContextRoute = BuildAlbumRouteForTrack(work);
        if (navigateBackToTrackContext && !string.IsNullOrWhiteSpace(trackContextRoute))
        {
            Nav.NavigateTo(trackContextRoute, replace: true);
        }
    }

    private string? BuildAlbumRouteForTrack(WorkViewModel work, bool edit = false)
    {
        if (work.CollectionId.HasValue)
        {
            var query = edit
                ? $"?track={work.Id:D}&edit=true"
                : $"?track={work.Id:D}";
            return $"/listen/music/albums/{work.CollectionId.Value:D}{query}";
        }

        if (string.IsNullOrWhiteSpace(work.Album))
        {
            return null;
        }

        return BuildAlbumDetailRoute(work.Album, FirstNonBlankOrNull(work.Artist, work.Author), work.Id, edit);
    }

    private async Task AddToPlaylistAsync(WorkViewModel work, ManagedCollectionViewModel collection)
        => await AddTracksToPlaylistAsync([work], collection);

    private async Task ToggleFavoriteAsync(WorkViewModel work)
    {
        var nextReaction = _favoriteWorkIds.Contains(work.Id) ? MediaReaction.Neutral : MediaReaction.Like;
        await Reactions.SetReactionAsync(work.Id, nextReaction, _activeProfileId);
        await RefreshReactionStateAsync();
    }

    private async Task ToggleDislikeAsync(WorkViewModel work)
    {
        var nextReaction = _dislikedWorkIds.Contains(work.Id) ? MediaReaction.Neutral : MediaReaction.Dislike;
        await Reactions.SetReactionAsync(work.Id, nextReaction, _activeProfileId);
        await RefreshReactionStateAsync();
    }

    private IReadOnlyList<WorkViewModel> ApplySongFilters(IEnumerable<WorkViewModel> works)
    {
        var filtered = works;

        if (!string.IsNullOrWhiteSpace(_songSearch))
        {
            filtered = filtered.Where(work =>
                work.Title.Contains(_songSearch, StringComparison.OrdinalIgnoreCase)
                || (work.Artist?.Contains(_songSearch, StringComparison.OrdinalIgnoreCase) ?? false)
                || (work.Album?.Contains(_songSearch, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.Equals(_songGenreFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(work => work.Genres.Any(genre => string.Equals(genre, _songGenreFilter, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.Equals(_songArtistFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(work => string.Equals(FirstNonBlank(work.Artist, work.Author, null), _songArtistFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(_songAlbumFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(work => string.Equals(work.Album, _songAlbumFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (string.Equals(_songFavoriteFilter, "favorites", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(work => _favoriteWorkIds.Contains(work.Id));
        }
        else if (string.Equals(_songFavoriteFilter, "nonfavorites", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(work => !_favoriteWorkIds.Contains(work.Id));
        }

        filtered = _songDateAddedFilter switch
        {
            "7" => filtered.Where(work => work.CreatedAt >= DateTimeOffset.UtcNow.AddDays(-7)),
            "30" => filtered.Where(work => work.CreatedAt >= DateTimeOffset.UtcNow.AddDays(-30)),
            "90" => filtered.Where(work => work.CreatedAt >= DateTimeOffset.UtcNow.AddDays(-90)),
            _ => filtered,
        };

        return filtered.ToList();
    }

    private IReadOnlyList<ContentGroupViewModel> ApplyAlbumFilters()
    {
        IEnumerable<ContentGroupViewModel> filtered = _albumGroups;

        if (!string.IsNullOrWhiteSpace(_albumSearch))
        {
            filtered = filtered.Where(group =>
                group.DisplayName.Contains(_albumSearch, StringComparison.OrdinalIgnoreCase)
                || (group.Creator?.Contains(_albumSearch, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.Equals(_albumArtistFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(group => string.Equals(group.Creator, _albumArtistFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(_albumGenreFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(group => AlbumMatchesGenre(group, _albumGenreFilter));
        }

        if (_albumFavoriteOnly)
        {
            filtered = filtered.Where(AlbumHasFavorites);
        }

        if (_albumArtOnly)
        {
            filtered = filtered.Where(group => !string.IsNullOrWhiteSpace(group.CoverUrl));
        }

        filtered = _albumSort switch
        {
            "title" => filtered.OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase),
            "artist" => filtered.OrderBy(group => group.Creator, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase),
            "year" => filtered.OrderByDescending(group => ParseYear(group.Year)).ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderByDescending(group => group.CreatedAt).ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase),
        };

        return filtered.ToList();
    }

    private IReadOnlyList<ContentGroupViewModel> ApplyArtistFilters()
    {
        IEnumerable<ContentGroupViewModel> filtered = _artistGroups;

        if (!string.IsNullOrWhiteSpace(_artistSearch))
        {
            filtered = filtered.Where(group => group.DisplayName.Contains(_artistSearch, StringComparison.OrdinalIgnoreCase));
        }

        filtered = _artistSort switch
        {
            "tracks" => filtered.OrderByDescending(group => group.WorkCount).ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase),
        };

        return filtered.ToList();
    }

    private bool AlbumMatchesGenre(ContentGroupViewModel album, string genre)
        => AlbumWorksFor(album).Any(work => work.Genres.Any(item => string.Equals(item, genre, StringComparison.OrdinalIgnoreCase)));

    private bool AlbumHasFavorites(ContentGroupViewModel album)
        => AlbumWorksFor(album).Any(work => _favoriteWorkIds.Contains(work.Id));

    private IReadOnlyList<WorkViewModel> AlbumWorksFor(ContentGroupViewModel album)
        => _musicWorks
            .Where(work => AlbumMatchesWork(album, work))
            .ToList();

    private static bool AlbumMatchesWork(ContentGroupViewModel album, WorkViewModel work)
    {
        if (work.CollectionId == album.CollectionId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(work.Album)
            || !string.Equals(work.Album, album.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(album.Creator))
        {
            return true;
        }

        var workCreator = FirstNonBlankOrNull(work.Artist, work.Author);
        return string.Equals(workCreator, album.Creator, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshReactionStateAsync()
    {
        _favoriteWorkIds = (await Reactions.GetFavoriteWorkIdsAsync(_activeProfileId)).ToHashSet();
        _dislikedWorkIds = (await Reactions.GetDislikedWorkIdsAsync(_activeProfileId)).ToHashSet();
        StateHasChanged();
    }

    private List<LibraryItemViewModel> BuildTrackGridItems(IEnumerable<WorkViewModel> works) =>
        works.Select(MapTrackToLibraryItem).ToList();

    private LibraryItemViewModel MapTrackToLibraryItem(WorkViewModel work)
    {
        var durationSeconds = GetTrackDurationSeconds(work);
        var artist = FirstNonBlank(work.Artist, work.Author, null);
        var comments = GetCanonicalValue(work, "comment", "comments", "description");
        var releaseDate = ParseCanonicalDate(GetCanonicalValue(work, "release_date", "date_released"));
        var lastPlayedAt = ParseCanonicalDate(GetCanonicalValue(work, "last_played", "played_at"));
        var dateModified = ParseCanonicalDate(GetCanonicalValue(work, "date_modified", "modified_at", "updated_at"));

        return new LibraryItemViewModel
        {
            EntityId = work.Id,
            Title = work.Title,
            OriginalTitle = work.OriginalTitle,
            Artist = string.Equals(artist, "-", StringComparison.Ordinal) ? null : artist,
            Album = work.Album,
            AlbumArtist = GetCanonicalValue(work, "album_artist"),
            Composer = GetCanonicalValue(work, "composer"),
            Comments = comments,
            Genre = work.Genres.FirstOrDefault() ?? work.Genre,
            Duration = LibraryHelpers.FormatDuration(durationSeconds, fallback: "0:00"),
            DurationSeconds = durationSeconds,
            TrackNumber = work.TrackNumber,
            DiscNumber = GetCanonicalValue(work, "disc_number"),
            Rating = GetCanonicalValue(work, "rating", "album_rating"),
            Year = work.Year,
            SortTitle = GetCanonicalValue(work, "sort_title") ?? work.Title,
            SortArtist = GetCanonicalValue(work, "sort_artist") ?? artist,
            SortAlbum = GetCanonicalValue(work, "sort_album") ?? work.Album,
            Kind = GetCanonicalValue(work, "kind") ?? "Audio File",
            MediaType = "Music",
            CoverUrl = work.CoverUrl,
            CoverThumbUrl = work.CoverUrl,
            CreatedAt = work.CreatedAt,
            DateModified = dateModified,
            LastPlayedAt = lastPlayedAt,
            ReleaseDate = releaseDate,
            PlayCount = GetPlayCount(work),
            IsFavorite = _favoriteWorkIds.Contains(work.Id),
        };
    }

    private IReadOnlyList<WorkViewModel> CurrentTrackSurfaceTracks =>
        IsAlbumDetail ? AlbumTracks :
        IsArtistsSurface ? ArtistTracks :
        IsPlaylistSurface ? ActivePlaylistTracks :
        FilteredSongs;

    private string CurrentTrackSurfaceLabel =>
        IsAlbumDetail && _albumDetail is not null ? _albumDetail.DisplayName :
        IsArtistsSurface && _artistDetail is not null ? _artistDetail.DisplayName :
        IsPlaylistSurface ? ActivePlaylistTitle :
        "All Music";

    private IReadOnlyList<WorkViewModel> SelectedTrackWorks =>
        CurrentTrackSurfaceTracks
            .Where(work => _selectedTrackIds.Contains(work.Id))
            .ToList();

    private async Task OnTrackSelectionChanged(HashSet<Guid> selected)
    {
        _selectedTrackIds = selected.ToHashSet();
        CloseTrackContextMenu();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnTrackRowActivated(Guid entityId)
    {
        var target = CurrentTrackSurfaceTracks.FirstOrDefault(work => work.Id == entityId);
        if (target is null)
            return;

        await PlayTracksAsync(CurrentTrackSurfaceTracks, target.Id, CurrentTrackSurfaceLabel);
    }

    private Task OnTrackContextRequested(LibraryRowContextMenuRequest request)
    {
        _trackContextMenuWorkId = request.EntityId;
        _trackContextMenuSourceLabel = CurrentTrackSurfaceLabel;
        _trackContextMenuX = request.ClientX;
        _trackContextMenuY = request.ClientY;

        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task OnTrackFavoriteToggled(Guid entityId)
    {
        var target = CurrentTrackSurfaceTracks.FirstOrDefault(work => work.Id == entityId);
        if (target is null)
            return;

        await ToggleFavoriteAsync(target);
    }

    private Task OnTrackDragStarted(Guid entityId)
    {
        _draggingTrackIds = _selectedTrackIds.Contains(entityId)
            ? _selectedTrackIds.ToHashSet()
            : [entityId];

        return Task.CompletedTask;
    }

    private Task OnTrackDragEnded()
    {
        _draggingTrackIds.Clear();
        return Task.CompletedTask;
    }

    private void CloseTrackContextMenu() => _trackContextMenuWorkId = null;

    private WorkViewModel? ContextMenuTrack =>
        _trackContextMenuWorkId.HasValue
            ? CurrentTrackSurfaceTracks.FirstOrDefault(work => work.Id == _trackContextMenuWorkId.Value)
            : null;

    private async Task PlaySelectedTracksAsync()
    {
        if (SelectedTrackWorks.Count == 0)
            return;

        await PlayTracksAsync(SelectedTrackWorks, SelectedTrackWorks[0].Id, "Selected Tracks");
        _selectedTrackIds.Clear();
    }

    private async Task QueueSelectedTracksAsync(bool next)
    {
        if (SelectedTrackWorks.Count == 0)
            return;

        foreach (var track in SelectedTrackWorks)
        {
            if (next)
                await Playback.InsertNextAsync(track);
            else
                await Playback.AddToQueueAsync(track);
        }

        Snackbar.Add(next ? "Selected songs will play next." : "Selected songs added to the queue.", Severity.Success);
        CloseTrackContextMenu();
    }

    private async Task EditSelectedTrackAsync()
    {
        var target = SelectedTrackWorks.FirstOrDefault();
        if (target is null)
            return;

        await EditTrackAsync(target);
    }

    private async Task DeleteSelectedTracksAsync()
    {
        if (SelectedTrackWorks.Count == 0)
            return;

        var deleteCount = SelectedTrackWorks.Count;

        var confirmed = await JS.InvokeAsync<bool>("confirm", $"Delete {deleteCount} selected song(s) from the library?");
        if (!confirmed)
            return;

        var response = await ApiClient.BatchDeleteLibraryCatalogItemsAsync(SelectedTrackWorks.Select(track => track.Id).ToArray());
        if (response is null)
        {
            Snackbar.Add("Delete failed.", Severity.Error);
            return;
        }

        _selectedTrackIds.Clear();
        CloseTrackContextMenu();
        await LoadAsync();
        Snackbar.Add($"{deleteCount} songs deleted.", Severity.Success);
    }

    private async Task AddSelectedTracksToPlaylistAsync(Guid? collectionId = null)
    {
        if (SelectedTrackWorks.Count == 0)
            return;

        if (collectionId.HasValue)
        {
            var collection = PlaylistCollections.FirstOrDefault(item => item.Id == collectionId.Value);
            if (collection is null)
                return;

            await AddTracksToPlaylistAsync(SelectedTrackWorks, collection);
            return;
        }

        await CreatePlaylistAndAddTracksAsync(SelectedTrackWorks);
    }

    private async Task AddContextMenuTrackToPlaylistAsync(Guid? collectionId = null)
    {
        if (ContextMenuTrack is null)
            return;

        if (collectionId.HasValue)
        {
            var collection = PlaylistCollections.FirstOrDefault(item => item.Id == collectionId.Value);
            if (collection is null)
                return;

            await AddTracksToPlaylistAsync([ContextMenuTrack], collection);
            CloseTrackContextMenu();
            return;
        }

        await CreatePlaylistAndAddTracksAsync([ContextMenuTrack]);
        CloseTrackContextMenu();
    }

    private async Task AddTracksToPlaylistAsync(IEnumerable<WorkViewModel> works, ManagedCollectionViewModel collection)
    {
        if (!_activeProfileId.HasValue)
        {
            Snackbar.Add("Choose an active profile before saving to playlists.", Severity.Warning);
            return;
        }

        var added = 0;
        foreach (var work in works)
        {
            if (await ApiClient.AddCollectionItemAsync(collection.Id, work.Id, _activeProfileId.Value))
                added++;
        }

        if (added == 0)
        {
            Snackbar.Add($"Could not add songs to {collection.Name}.", Severity.Error);
            return;
        }

        Snackbar.Add($"Added {added} song(s) to {collection.Name}.", Severity.Success);
        await LoadAsync();
    }

    private async Task CreatePlaylistAndAddTracksAsync(IEnumerable<WorkViewModel> works)
    {
        if (!_activeProfileId.HasValue)
        {
            Snackbar.Add("Choose an active profile before creating playlists.", Severity.Warning);
            return;
        }

        var timestamp = DateTimeOffset.Now.ToString("M-d HHmm", CultureInfo.InvariantCulture);
        var created = await ApiClient.CreateCollectionAsync(
            name: $"New Playlist {timestamp}",
            description: "Created from the Listen grid.",
            iconName: Icons.Material.Outlined.QueueMusic,
            collectionType: "Playlist",
            rules: [],
            matchMode: "all",
            sortField: null,
            sortDirection: "asc",
            liveUpdating: false,
            visibility: "private",
            profileId: _activeProfileId.Value);

        if (!created)
        {
            Snackbar.Add("Could not create a playlist for the dropped songs.", Severity.Error);
            return;
        }

        await LoadAsync();
        var createdPlaylist = PlaylistCollections.OrderByDescending(collection => collection.CreatedAt).FirstOrDefault();
        if (createdPlaylist is null)
        {
            Snackbar.Add("Playlist was created, but it could not be loaded yet.", Severity.Warning);
            return;
        }

        await AddTracksToPlaylistAsync(works, createdPlaylist);
    }

    private async Task HandlePlaylistDropAsync(Guid? collectionId = null)
    {
        if (_draggingTrackIds.Count == 0)
            return;

        _selectedTrackIds = _draggingTrackIds.ToHashSet();
        if (collectionId.HasValue)
            await AddSelectedTracksToPlaylistAsync(collectionId.Value);
        else
            await AddSelectedTracksToPlaylistAsync();

        _draggingTrackIds.Clear();
    }

    private void OpenArtist(string artistName)
        => NavigateTo($"/listen/music/artists/{Uri.EscapeDataString(artistName)}");

    private Task OnTrackArtistClicked(string artistName)
    {
        if (!string.IsNullOrWhiteSpace(artistName))
            OpenArtist(artistName);

        return Task.CompletedTask;
    }

    private Task OpenTrackColumnsAsync(MouseEventArgs args)
        => _trackGrid?.OpenColumnsPanelAsync(args) ?? Task.CompletedTask;

    private IReadOnlyList<WorkViewModel> ResolveGroupWorks(IEnumerable<CollectionGroupWorkViewModel> works)
        => works
            .Select(groupWork => _workLookup.GetValueOrDefault(groupWork.WorkId))
            .Where(work => work is not null)
            .Cast<WorkViewModel>()
            .ToList();

    private IReadOnlyList<WorkViewModel> ResolveArtistTracks()
    {
        if (_artistDetail is null)
        {
            return [];
        }

        var directWorks = ResolveGroupWorks(_artistDetail.Works);
        if (directWorks.Count > 0)
        {
            return directWorks;
        }

        return _artistDetail.Seasons
            .SelectMany(season => season.Episodes)
            .Select(episode => _workLookup.GetValueOrDefault(episode.WorkId))
            .Where(work => work is not null)
            .Cast<WorkViewModel>()
            .ToList();
    }

    private IReadOnlyList<WorkViewModel> ResolveActivePlaylistTracks()
    {
        if (!string.IsNullOrWhiteSpace(PlaylistKey))
        {
            return PlaylistKey.ToLowerInvariant() switch
            {
                "all-music" => SortedSongs.ToList(),
                "favorite-songs" => _musicWorks
                    .Where(work => _favoriteWorkIds.Contains(work.Id))
                    .OrderBy(work => work.Artist ?? work.Author, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(work => work.Album, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(work => ParseTrackNumber(work.TrackNumber))
                    .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                "recently-added" => RecentlyAddedTracks.ToList(),
                _ => [],
            };
        }

        return _playlistItems
            .OrderBy(item => item.SortOrder)
            .Select(item => _workLookup.GetValueOrDefault(item.WorkId))
            .Where(work => work is not null)
            .Cast<WorkViewModel>()
            .ToList();
    }

    private IReadOnlyList<WorkViewModel> SortSongs(IEnumerable<WorkViewModel> works)
    {
        var ordered = NormalizeSongSortColumn(_songSortColumn) switch
        {
            "title" => _songSortDescending
                ? works.OrderByDescending(work => work.Title, StringComparer.OrdinalIgnoreCase)
                : works.OrderBy(work => work.Title, StringComparer.OrdinalIgnoreCase),
            "artist" => _songSortDescending
                ? works.OrderByDescending(work => work.Artist ?? work.Author, StringComparer.OrdinalIgnoreCase).ThenByDescending(work => work.Title, StringComparer.OrdinalIgnoreCase)
                : works.OrderBy(work => work.Artist ?? work.Author, StringComparer.OrdinalIgnoreCase).ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase),
            "album" => _songSortDescending
                ? works.OrderByDescending(work => work.Album, StringComparer.OrdinalIgnoreCase).ThenByDescending(work => ParseTrackNumber(work.TrackNumber))
                : works.OrderBy(work => work.Album, StringComparer.OrdinalIgnoreCase).ThenBy(work => ParseTrackNumber(work.TrackNumber)),
            "genre" => _songSortDescending
                ? works.OrderByDescending(work => work.Genres.FirstOrDefault(), StringComparer.OrdinalIgnoreCase).ThenByDescending(work => work.Title, StringComparer.OrdinalIgnoreCase)
                : works.OrderBy(work => work.Genres.FirstOrDefault(), StringComparer.OrdinalIgnoreCase).ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase),
            "favorite" => _songSortDescending
                ? works.OrderByDescending(work => _favoriteWorkIds.Contains(work.Id)).ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
                : works.OrderBy(work => _favoriteWorkIds.Contains(work.Id)).ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase),
            "plays" => _songSortDescending
                ? works.OrderByDescending(GetPlayCount).ThenByDescending(work => work.Title, StringComparer.OrdinalIgnoreCase)
                : works.OrderBy(GetPlayCount).ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase),
            "time" => _songSortDescending
                ? works.OrderByDescending(GetDurationSortKey).ThenByDescending(work => work.Title, StringComparer.OrdinalIgnoreCase)
                : works.OrderBy(GetDurationSortKey).ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase),
            _ => _songSortDescending
                ? works.OrderByDescending(work => work.CreatedAt).ThenByDescending(work => work.Title, StringComparer.OrdinalIgnoreCase)
                : works.OrderBy(work => work.CreatedAt).ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ToList();
    }

    private static string NormalizeSongSortColumn(string? column) =>
        (column ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "date_added" => "dateAdded",
            "track_number" => "title",
            _ => string.IsNullOrWhiteSpace(column) ? "dateAdded" : column,
        };

    private static bool IsMusicWork(WorkViewModel work)
    {
        var mediaType = work.MediaType ?? string.Empty;
        return mediaType.Contains("Music", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMusicWork(string? mediaType)
        => (mediaType ?? string.Empty).Contains("Music", StringComparison.OrdinalIgnoreCase)
           || string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase);

    private static bool IsAudiobookWork(WorkViewModel work)
        => (work.MediaType ?? string.Empty).Contains("Audiobook", StringComparison.OrdinalIgnoreCase)
           || string.Equals(work.MediaType, "M4B", StringComparison.OrdinalIgnoreCase);

    private static int ParseTrackNumber(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue;

    private static int ParseYear(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static int GetDurationSortKey(WorkViewModel work)
    {
        var durationSeconds = GetTrackDurationSeconds(work);
        return durationSeconds > 0 ? (int)Math.Min(durationSeconds, int.MaxValue) : 0;
    }

    private static string GetTrackDuration(WorkViewModel work)
        => LibraryHelpers.FormatDuration(GetTrackDurationSeconds(work), fallback: "0:00");

    private static long GetTrackDurationSeconds(WorkViewModel work)
        => LibraryHelpers.NormalizeDurationSeconds(
            GetCanonicalValue(work, "duration_sec"),
            GetCanonicalValue(work, "duration_seconds"),
            GetCanonicalValue(work, "duration"),
            GetCanonicalValue(work, "runtime")) ?? 0;

    private static string? GetCanonicalValue(WorkViewModel work, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = work.CanonicalValues.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static DateTimeOffset? ParseCanonicalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            return parsed;

        if (DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
            return parsed;

        return null;
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "-";

    private static string? FirstNonBlankOrNull(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string FormatDateAdded(DateTimeOffset createdAt)
        => createdAt == default ? "-" : createdAt.LocalDateTime.ToString("M/d/yyyy", CultureInfo.CurrentCulture);

    private static int GetPlayCount(WorkViewModel work)
    {
        var value = work.CanonicalValues.FirstOrDefault(item =>
            string.Equals(item.Key, "play_count", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Key, "plays", StringComparison.OrdinalIgnoreCase))?.Value;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plays) ? plays : 0;
    }

    private static string GetPlayCountDisplay(WorkViewModel work)
        => GetPlayCount(work) > 0 ? GetPlayCount(work).ToString(CultureInfo.InvariantCulture) : "-";

    private static string PlaylistIconFor(ManagedCollectionViewModel collection)
        => collection.CollectionType switch
        {
            "Smart" => Icons.Material.Outlined.AutoAwesomeMotion,
            "PlaylistFolder" => Icons.Material.Outlined.Folder,
            "Mix" => Icons.Material.Outlined.GraphicEq,
            "System" => Icons.Material.Outlined.SettingsSuggest,
            _ => Icons.Material.Outlined.QueueMusic,
        };

    private static bool IsUserVisiblePlaylist(ManagedCollectionViewModel collection)
    {
        if (string.Equals(collection.Name, "Watchlist", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.Name, "Favorites", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.Name, "Disliked Media", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, "Smart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(collection.CollectionType, "PlaylistFolder", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanAcceptPlaylistItems(ManagedCollectionViewModel collection)
        => string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase);

    private static string Pluralize(int count, string singular)
        => count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    private static ListenPlaylistCard ToPlaylistCard(ManagedCollectionViewModel playlist)
        => new(
            playlist.Name,
            $"{playlist.TypeLabel} Playlist",
            $"{playlist.ItemCount} items",
            $"/listen/music/playlists/{playlist.Id}",
            playlist.Description ?? "Playlist detail with queue-first listening controls.",
            playlist.SquareArtworkUrl);

    private sealed record ListenNavItem(string Label, string Route, string Icon, string? Meta = null, bool IsChild = false);
    private sealed record ListenPlaylistCard(string Title, string Type, string Meta, string Route, string Description, string? SquareArtworkUrl);
}
