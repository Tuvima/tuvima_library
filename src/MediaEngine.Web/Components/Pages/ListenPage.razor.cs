using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Navigation;
using MediaEngine.Web.Services.Playback;
using MudBlazor;

namespace MediaEngine.Web.Components.Pages;

public partial class ListenPage
{
    [Inject] private IEngineApiClient ApiClient { get; set; } = default!;
    [Inject] private UIOrchestratorService Orchestrator { get; set; } = default!;
    [Inject] private ListenPlaybackService Playback { get; set; } = default!;
    [Inject] private MediaReactionService Reactions { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public string? Section { get; set; }
    [Parameter] public Guid? CollectionId { get; set; }
    [Parameter] public string? ArtistKey { get; set; }
    [Parameter] public string? PlaylistKey { get; set; }
    [Parameter] public Guid? WorkId { get; set; }
    [SupplyParameterFromQuery(Name = "track")] public Guid? Track { get; set; }

    private readonly List<WorkViewModel> _allWorks = [];
    private readonly List<WorkViewModel> _musicWorks = [];
    private readonly List<WorkViewModel> _audiobookWorks = [];
    private readonly List<ContentGroupViewModel> _albumGroups = [];
    private readonly List<ContentGroupViewModel> _artistGroups = [];
    private readonly List<ManagedCollectionViewModel> _managedCollections = [];
    private readonly List<CollectionItemViewModel> _playlistItems = [];
    private readonly Dictionary<Guid, WorkViewModel> _workLookup = [];

    private CollectionGroupDetailViewModel? _albumDetail;
    private CollectionGroupDetailViewModel? _artistDetail;
    private Guid? _activeProfileId;
    private HashSet<Guid> _favoriteWorkIds = [];
    private HashSet<Guid> _dislikedWorkIds = [];
    private bool _loading = true;
    private bool _redirecting;
    private bool _railOpen;
    private string? _error;
    private string _songSortColumn = "dateAdded";
    private bool _songSortDescending = true;
    private string? _lastHandledTrackContext;

    private string CurrentPath => Nav.ToAbsoluteUri(Nav.Uri).AbsolutePath;
    private string NormalizedSection => string.IsNullOrWhiteSpace(Section) ? string.Empty : Section.Trim().ToLowerInvariant();
    private bool IsLanding => string.Equals(CurrentPath, "/listen", StringComparison.OrdinalIgnoreCase) || string.Equals(CurrentPath, "/listen/music", StringComparison.OrdinalIgnoreCase);
    private bool IsAlbumsView => !CollectionId.HasValue && string.Equals(NormalizedSection, "albums", StringComparison.OrdinalIgnoreCase);
    private bool IsAlbumDetail => CollectionId.HasValue && CurrentPath.StartsWith("/listen/music/albums/", StringComparison.OrdinalIgnoreCase);
    private bool IsSongsView => string.Equals(NormalizedSection, "songs", StringComparison.OrdinalIgnoreCase);
    private bool IsPlaylistsView => !CollectionId.HasValue && string.IsNullOrWhiteSpace(PlaylistKey) && string.Equals(NormalizedSection, "playlists", StringComparison.OrdinalIgnoreCase);
    private bool IsPlaylistSurface => (CollectionId.HasValue && CurrentPath.StartsWith("/listen/music/playlists/", StringComparison.OrdinalIgnoreCase)) || !string.IsNullOrWhiteSpace(PlaylistKey);
    private bool IsAudiobooksView => string.Equals(CurrentPath, "/listen/audiobooks", StringComparison.OrdinalIgnoreCase);
    private bool IsArtistsSurface => string.Equals(NormalizedSection, "artists", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(ArtistKey);
    private string SelectedArtistName => !string.IsNullOrWhiteSpace(ArtistKey)
        ? Uri.UnescapeDataString(ArtistKey)
        : _artistGroups.FirstOrDefault()?.DisplayName ?? string.Empty;

    private string PageTitleText => IsAlbumDetail && _albumDetail is not null
        ? $"{_albumDetail.DisplayName} - Listen"
        : IsPlaylistSurface
            ? $"{ActivePlaylistTitle} - Listen"
            : IsArtistsSurface && !string.IsNullOrWhiteSpace(SelectedArtistName)
                ? $"{SelectedArtistName} - Listen"
                : IsAudiobooksView
                    ? "Audiobooks - Listen"
                    : "Listen - Tuvima";

    private string CurrentEyebrow => IsAlbumDetail
        ? "Album"
        : IsPlaylistSurface
            ? ActivePlaylistTypeLabel
            : IsArtistsSurface
                ? "Artists"
                : IsSongsView
                    ? "Songs"
                    : IsPlaylistsView
                        ? "Playlists"
                        : IsAudiobooksView
                            ? "Audiobooks"
                            : "Listen";

    private string CurrentHeading => IsAlbumDetail && _albumDetail is not null
        ? _albumDetail.DisplayName
        : IsPlaylistSurface
            ? ActivePlaylistTitle
            : IsArtistsSurface && !string.IsNullOrWhiteSpace(SelectedArtistName)
                ? SelectedArtistName
                : IsSongsView
                    ? "Songs"
                    : IsPlaylistsView
                        ? "Playlists"
                        : IsAudiobooksView
                            ? "Audiobooks"
                            : "Library";

    private IReadOnlyList<WorkViewModel> SortedSongs => SortSongs(_musicWorks).ToList();
    private IReadOnlyList<WorkViewModel> RecentlyAddedTracks => _musicWorks.OrderByDescending(work => work.CreatedAt).ToList();
    private IReadOnlyList<WorkViewModel> AlbumTracks => _albumDetail is null ? [] : ResolveGroupWorks(_albumDetail.Works);
    private IReadOnlyList<CollectionGroupSeasonViewModel> ArtistAlbums => _artistDetail?.Seasons ?? [];
    private IReadOnlyList<WorkViewModel> ArtistTracks => ResolveArtistTracks();
    private IReadOnlyList<WorkViewModel> ActivePlaylistTracks => ResolveActivePlaylistTracks();
    private IReadOnlyList<string> PlaylistCoverUrls => ActivePlaylistTracks.Select(track => track.CoverUrl).Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().Cast<string>().Take(4).ToList();
    private IReadOnlyList<ManagedCollectionViewModel> PlaylistCollections => _managedCollections
        .Where(IsUserVisiblePlaylist)
        .OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

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

    private IReadOnlyList<ListenNavItem> PinnedItems =>
    [
        new("Recently Added", "/listen/music/playlists/system/recently-added", Icons.Material.Outlined.Schedule, RecentlyAddedTracks.Count.ToString(CultureInfo.InvariantCulture)),
        new("Favorite Songs", "/listen/music/playlists/system/favorite-songs", Icons.Material.Outlined.Grade, _favoriteWorkIds.Count.ToString(CultureInfo.InvariantCulture)),
        new("All Music", "/listen/music/playlists/system/all-music", Icons.Material.Outlined.LibraryMusic, _musicWorks.Count.ToString(CultureInfo.InvariantCulture)),
    ];

    private IReadOnlyList<ListenNavItem> LibraryItems =>
    [
        new("Albums", "/listen/music/albums", Icons.Material.Outlined.Album, _albumGroups.Count.ToString(CultureInfo.InvariantCulture)),
        new("Artists", "/listen/music/artists", Icons.Material.Outlined.PersonOutline, _artistGroups.Count.ToString(CultureInfo.InvariantCulture)),
        new("Songs", "/listen/music/songs", Icons.Material.Outlined.MusicNote, _musicWorks.Count.ToString(CultureInfo.InvariantCulture)),
        new("Audiobooks", "/listen/audiobooks", Icons.Material.Outlined.Headphones, _audiobookWorks.Count.ToString(CultureInfo.InvariantCulture)),
    ];

    private IReadOnlyList<ListenOverviewCard> OverviewCards =>
    [
        new("Albums", $"{_albumGroups.Count} collections", "/listen/music/albums", Icons.Material.Outlined.Album),
        new("Artists", $"{_artistGroups.Count} artists", "/listen/music/artists", Icons.Material.Outlined.PersonOutline),
        new("Songs", $"{_musicWorks.Count} tracks", "/listen/music/songs", Icons.Material.Outlined.MusicNote),
        new("Playlists", $"{PlaylistCollections.Count} saved views", "/listen/music/playlists", Icons.Material.Outlined.QueueMusic),
        new("Audiobooks", $"{_audiobookWorks.Count} titles", "/listen/audiobooks", Icons.Material.Outlined.Headphones),
    ];

    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();
    }

    private IReadOnlyList<ListenNavItem> PlaylistNavItems
    {
        get
        {
            var items = new List<ListenNavItem>
            {
                new("All Playlists", "/listen/music/playlists", Icons.Material.Outlined.QueueMusic),
                new("Favorite Songs", "/listen/music/playlists/system/favorite-songs", Icons.Material.Outlined.Grade, null, true),
                new("All Music", "/listen/music/playlists/system/all-music", Icons.Material.Outlined.LibraryMusic, null, true),
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

    private IReadOnlyList<ListenPlaylistCard> SystemPlaylistCards =>
    [
        new("All Music", "System Playlist", $"{_musicWorks.Count} tracks", "/listen/music/playlists/system/all-music", "Every song you own in one place."),
        new("Favorite Songs", "System Playlist", $"{_favoriteWorkIds.Count} liked", "/listen/music/playlists/system/favorite-songs", "Positive reactions from the active profile."),
        new("Recently Added", "System Playlist", $"{RecentlyAddedTracks.Count} tracks", "/listen/music/playlists/system/recently-added", "Fresh arrivals from your library."),
    ];

    private async Task LoadAsync()
    {
        _error = null;
        _loading = true;
        _redirecting = false;
        _albumDetail = null;
        _artistDetail = null;
        _playlistItems.Clear();
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
            _activeProfileId = profile?.Id;

            var worksTask = Orchestrator.GetLibraryWorksAsync();
            var albumGroupsTask = ApiClient.GetSystemViewGroupsAsync(mediaType: "Music", groupField: "album");
            var artistGroupsTask = ApiClient.GetSystemViewGroupsAsync(mediaType: "Music", groupField: "artist");
            var collectionsTask = _activeProfileId.HasValue
                ? ApiClient.GetManagedCollectionsAsync(_activeProfileId.Value)
                : Task.FromResult(new List<ManagedCollectionViewModel>());

            await Task.WhenAll(worksTask, albumGroupsTask, artistGroupsTask, collectionsTask);

            _allWorks.Clear();
            _allWorks.AddRange(worksTask.Result);

            _musicWorks.Clear();
            _musicWorks.AddRange(_allWorks.Where(IsMusicWork).OrderBy(work => work.Artist ?? work.Author).ThenBy(work => work.Album).ThenBy(work => ParseTrackNumber(work.TrackNumber)).ThenBy(work => work.Title));

            _audiobookWorks.Clear();
            _audiobookWorks.AddRange(_allWorks.Where(IsAudiobookWork).OrderBy(work => work.Author).ThenBy(work => work.Series).ThenBy(work => work.Title));

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

            if (IsAlbumDetail && CollectionId.HasValue)
            {
                _albumDetail = await ApiClient.GetCollectionGroupDetailAsync(CollectionId.Value);
            }

            if (IsArtistsSurface && !string.IsNullOrWhiteSpace(SelectedArtistName))
            {
                _artistDetail = await ApiClient.GetArtistDetailByNameAsync(SelectedArtistName);
            }

            if (CollectionId.HasValue && IsPlaylistSurface)
            {
                _playlistItems.AddRange(await ApiClient.GetCollectionItemsAsync(CollectionId.Value, 1000, _activeProfileId));
            }

            _loading = false;
            await HandleTrackQueryAsync();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _loading = false;
        }
    }

    private async Task ReloadAsync() => await LoadAsync();

    private async Task HandleTrackQueryAsync()
    {
        if (!Track.HasValue)
        {
            _lastHandledTrackContext = null;
            return;
        }

        var context = $"{CurrentPath}|{Track.Value}";
        if (string.Equals(_lastHandledTrackContext, context, StringComparison.Ordinal))
        {
            return;
        }

        _lastHandledTrackContext = context;

        if (!_workLookup.TryGetValue(Track.Value, out var requestedTrack))
        {
            return;
        }

        if (IsAlbumDetail && AlbumTracks.Count > 0)
        {
            await PlayTracksAsync(AlbumTracks, Track, _albumDetail?.DisplayName ?? requestedTrack.Album ?? requestedTrack.Title);
            return;
        }

        if (IsPlaylistSurface && ActivePlaylistTracks.Count > 0)
        {
            await PlayTracksAsync(ActivePlaylistTracks, Track, ActivePlaylistTitle);
            return;
        }

        if (IsArtistsSurface && ArtistTracks.Count > 0)
        {
            await PlayTracksAsync(ArtistTracks, Track, SelectedArtistName);
            return;
        }

        if (IsSongsView)
        {
            await PlayTracksAsync(SortedSongs, Track, "All Music");
            return;
        }

        await Playback.PlayWorkAsync(requestedTrack, requestedTrack.Album ?? requestedTrack.Title);
    }

    private void ToggleRail() => _railOpen = !_railOpen;
    private void CloseRail() => _railOpen = false;

    private void NavigateTo(string route)
    {
        CloseRail();
        Nav.NavigateTo(route);
    }

    private bool IsRouteActive(string route)
        => string.Equals(CurrentPath, route, StringComparison.OrdinalIgnoreCase)
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

    private async Task AddToPlaylistAsync(WorkViewModel work, ManagedCollectionViewModel collection)
    {
        if (!_activeProfileId.HasValue)
        {
            Snackbar.Add("Choose an active profile before saving to playlists.", Severity.Warning);
            return;
        }

        if (await ApiClient.AddCollectionItemAsync(collection.Id, work.Id, _activeProfileId.Value))
        {
            Snackbar.Add($"Added to {collection.Name}", Severity.Success);
        }
        else
        {
            Snackbar.Add($"Could not add {work.Title} to {collection.Name}", Severity.Error);
        }
    }

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

    private async Task RefreshReactionStateAsync()
    {
        _favoriteWorkIds = (await Reactions.GetFavoriteWorkIdsAsync(_activeProfileId)).ToHashSet();
        _dislikedWorkIds = (await Reactions.GetDislikedWorkIdsAsync(_activeProfileId)).ToHashSet();
        StateHasChanged();
    }

    private void OpenArtist(string artistName)
        => Nav.NavigateTo($"/listen/music/artists/{Uri.EscapeDataString(artistName)}");

    private void HandleSongSort(string column)
    {
        if (string.Equals(_songSortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            _songSortDescending = !_songSortDescending;
            return;
        }

        _songSortColumn = column;
        _songSortDescending = string.Equals(column, "dateAdded", StringComparison.OrdinalIgnoreCase) || string.Equals(column, "favorite", StringComparison.OrdinalIgnoreCase);
    }

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
                "all-music" => _musicWorks.OrderByDescending(work => work.CreatedAt).ToList(),
                "favorite-songs" => _musicWorks.Where(work => _favoriteWorkIds.Contains(work.Id)).OrderBy(work => work.Artist ?? work.Author).ThenBy(work => work.Album).ThenBy(work => ParseTrackNumber(work.TrackNumber)).ThenBy(work => work.Title).ToList(),
                "recently-added" => _musicWorks.OrderByDescending(work => work.CreatedAt).ToList(),
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
        var ordered = _songSortColumn switch
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
            _ => _songSortDescending
                ? works.OrderByDescending(work => work.CreatedAt).ThenByDescending(work => work.Title, StringComparer.OrdinalIgnoreCase)
                : works.OrderBy(work => work.CreatedAt).ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ToList();
    }

#pragma warning disable ASP0006
    private RenderFragment RenderAlbumCard(ContentGroupViewModel album) => builder =>
    {
        var seq = 0;
        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "listen-card listen-card--album");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => NavigateTo($"/listen/music/albums/{album.CollectionId}")));
        BuildArtwork(builder, ref seq, "listen-card__art", album.CoverUrl ?? album.ArtistPhotoUrl, album.DisplayName, Icons.Material.Outlined.Album);
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-card__title");
        builder.AddContent(seq++, album.DisplayName);
        builder.CloseElement();
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-card__meta");
        builder.AddContent(seq++, FirstNonBlank(album.Creator, album.Year, Pluralize(album.WorkCount, "track")));
        builder.CloseElement();
        builder.CloseElement();
    };

    private RenderFragment RenderArtistAlbumCard(CollectionGroupSeasonViewModel album) => builder =>
    {
        var seq = 0;
        var route = album.AlbumCollectionId.HasValue ? $"/listen/music/albums/{album.AlbumCollectionId.Value}" : null;
        builder.OpenElement(seq++, route is null ? "div" : "button");
        builder.AddAttribute(seq++, "class", "listen-card listen-card--album");
        if (route is not null)
        {
            builder.AddAttribute(seq++, "type", "button");
            builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => NavigateTo(route)));
        }
        BuildArtwork(builder, ref seq, "listen-card__art", album.CoverUrl, album.SeasonLabel ?? $"Album {album.SeasonNumber}", Icons.Material.Outlined.Album);
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-card__title");
        builder.AddContent(seq++, album.SeasonLabel ?? $"Album {album.SeasonNumber}");
        builder.CloseElement();
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-card__meta");
        builder.AddContent(seq++, FirstNonBlank(album.Year, Pluralize(album.Episodes.Count, "track")));
        builder.CloseElement();
        builder.CloseElement();
    };

    private RenderFragment RenderPlaylistCard(ListenPlaylistCard playlist) => builder =>
    {
        var seq = 0;
        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "listen-card listen-card--playlist");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => NavigateTo(playlist.Route)));
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-card__art listen-card__art--playlist");
        builder.OpenComponent<MudIcon>(seq++);
        builder.AddAttribute(seq++, "Icon", Icons.Material.Outlined.QueueMusic);
        builder.AddAttribute(seq++, "Style", "font-size: 42px;");
        builder.CloseComponent();
        builder.CloseElement();
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-card__meta listen-card__meta--label");
        builder.AddContent(seq++, playlist.Type);
        builder.CloseElement();
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-card__title");
        builder.AddContent(seq++, playlist.Title);
        builder.CloseElement();
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-card__meta");
        builder.AddContent(seq++, playlist.Meta);
        builder.CloseElement();
        builder.OpenElement(seq++, "p");
        builder.AddContent(seq++, playlist.Description);
        builder.CloseElement();
        builder.CloseElement();
    };

    private RenderFragment RenderTrackTable(IReadOnlyList<WorkViewModel> tracks, string sourceLabel, bool showAlbum, bool showAdded, bool showGenre, bool sortable = false) => builder =>
    {
        var seq = 0;
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-table-shell");
        builder.OpenElement(seq++, "table");
        builder.AddAttribute(seq++, "class", "listen-table");
        builder.OpenElement(seq++, "thead");
        builder.OpenElement(seq++, "tr");
        BuildTableHeader(builder, ref seq, "Title", "title", sortable);
        BuildTableHeader(builder, ref seq, "Time");
        BuildTableHeader(builder, ref seq, "Artist", "artist", sortable);
        if (showAlbum) BuildTableHeader(builder, ref seq, "Album", "album", sortable);
        if (showGenre) BuildTableHeader(builder, ref seq, "Genre", "genre", sortable);
        BuildTableHeader(builder, ref seq, "Favorite", "favorite", sortable);
        BuildTableHeader(builder, ref seq, "Plays", "plays", sortable);
        if (showAdded) BuildTableHeader(builder, ref seq, "Date Added", "dateAdded", sortable);
        BuildTableHeader(builder, ref seq, string.Empty);
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(seq++, "tbody");
        foreach (var track in tracks)
        {
            builder.OpenElement(seq++, "tr");
            builder.AddAttribute(seq++, "class", $"listen-table__row {(_dislikedWorkIds.Contains(track.Id) ? "is-muted" : null)}");

            builder.OpenElement(seq++, "td");
            BuildTrackTitleCell(builder, ref seq, track, sourceLabel, tracks);
            builder.CloseElement();

            BuildSimpleCell(builder, ref seq, GetTrackDuration(track));
            BuildLinkedCell(builder, ref seq, FirstNonBlank(track.Artist, track.Author, "Unknown"), () => OpenArtist(FirstNonBlank(track.Artist, track.Author)));
            if (showAlbum)
            {
                BuildLinkedCell(builder, ref seq, track.Album ?? "Single", () =>
                {
                    if (track.CollectionId.HasValue)
                    {
                        Nav.NavigateTo(MediaNavigation.ForCollectionMedia(track.MediaType, track.CollectionId.Value, track.Id));
                    }
                });
            }
            if (showGenre) BuildSimpleCell(builder, ref seq, track.Genres.FirstOrDefault() ?? "—");

            builder.OpenElement(seq++, "td");
            builder.OpenElement(seq++, "button");
            builder.AddAttribute(seq++, "type", "button");
            builder.AddAttribute(seq++, "class", $"listen-favorite {(_favoriteWorkIds.Contains(track.Id) ? "is-active" : null)}");
            builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => ToggleFavoriteAsync(track)));
            builder.AddContent(seq++, _favoriteWorkIds.Contains(track.Id) ? "★" : "☆");
            builder.CloseElement();
            builder.CloseElement();

            BuildSimpleCell(builder, ref seq, GetPlayCountDisplay(track));
            if (showAdded) BuildSimpleCell(builder, ref seq, FormatDateAdded(track.CreatedAt));

            builder.OpenElement(seq++, "td");
            builder.AddContent(seq++, RenderTrackRowMenu(track, tracks, sourceLabel));
            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();
    };

    private RenderFragment RenderAlbumTrackTable() => builder =>
    {
        var seq = 0;
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-table-shell");
        builder.OpenElement(seq++, "table");
        builder.AddAttribute(seq++, "class", "listen-table");
        builder.OpenElement(seq++, "thead");
        builder.OpenElement(seq++, "tr");
        BuildTableHeader(builder, ref seq, "#");
        BuildTableHeader(builder, ref seq, "Title");
        BuildTableHeader(builder, ref seq, "Time");
        BuildTableHeader(builder, ref seq, "Favorite");
        BuildTableHeader(builder, ref seq, string.Empty);
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(seq++, "tbody");
        foreach (var track in (_albumDetail?.Works ?? []).OrderBy(work => ParseTrackNumber(work.TrackNumber)))
        {
            var libraryTrack = _workLookup.GetValueOrDefault(track.WorkId);
            builder.OpenElement(seq++, "tr");
            builder.AddAttribute(seq++, "class", $"listen-table__row {(libraryTrack is null ? "is-disabled" : null)}");
            BuildSimpleCell(builder, ref seq, track.TrackNumber ?? track.Ordinal?.ToString(CultureInfo.InvariantCulture) ?? "—");

            builder.OpenElement(seq++, "td");
            if (libraryTrack is not null)
            {
                builder.OpenElement(seq++, "button");
                builder.AddAttribute(seq++, "type", "button");
                builder.AddAttribute(seq++, "class", "listen-track-link");
                builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => PlayTracksAsync(AlbumTracks, libraryTrack.Id, _albumDetail!.DisplayName)));
                builder.AddContent(seq++, track.Title);
                builder.CloseElement();
            }
            else
            {
                builder.AddContent(seq++, track.Title);
            }
            builder.CloseElement();

            BuildSimpleCell(builder, ref seq, track.Duration ?? "—");

            builder.OpenElement(seq++, "td");
            if (libraryTrack is not null)
            {
                builder.OpenElement(seq++, "button");
                builder.AddAttribute(seq++, "type", "button");
                builder.AddAttribute(seq++, "class", $"listen-favorite {(_favoriteWorkIds.Contains(libraryTrack.Id) ? "is-active" : null)}");
                builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => ToggleFavoriteAsync(libraryTrack)));
                builder.AddContent(seq++, _favoriteWorkIds.Contains(libraryTrack.Id) ? "★" : "☆");
                builder.CloseElement();
            }
            else
            {
                builder.AddContent(seq++, "—");
            }
            builder.CloseElement();

            builder.OpenElement(seq++, "td");
            if (libraryTrack is not null)
            {
                builder.AddContent(seq++, RenderTrackRowMenu(libraryTrack, AlbumTracks, _albumDetail!.DisplayName));
            }
            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();
    };

    private RenderFragment RenderTrackRowMenu(WorkViewModel track, IReadOnlyList<WorkViewModel> sourceTracks, string sourceLabel) => builder =>
    {
        var seq = 0;
        builder.OpenElement(seq++, "details");
        builder.AddAttribute(seq++, "class", "listen-row-menu");
        builder.AddAttribute(seq++, "onclick:stopPropagation", true);
        builder.OpenElement(seq++, "summary");
        builder.AddAttribute(seq++, "class", "listen-row-menu__summary");
        builder.OpenComponent<MudIcon>(seq++);
        builder.AddAttribute(seq++, "Icon", Icons.Material.Outlined.MoreHoriz);
        builder.AddAttribute(seq++, "Style", "font-size: 18px;");
        builder.CloseComponent();
        builder.CloseElement();

        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-row-menu__panel");
        BuildMenuButton(builder, ref seq, "Play now", () => PlayTracksAsync(sourceTracks, track.Id, sourceLabel));
        BuildMenuButton(builder, ref seq, "Play next", () => Playback.InsertNextAsync(track));
        BuildMenuButton(builder, ref seq, _favoriteWorkIds.Contains(track.Id) ? "Remove favorite" : "Favorite", () => ToggleFavoriteAsync(track));
        BuildMenuButton(builder, ref seq, _dislikedWorkIds.Contains(track.Id) ? "Clear dislike" : "Dislike", () => ToggleDislikeAsync(track));

        foreach (var playlist in PlaylistCollections.Take(8))
        {
            BuildMenuButton(builder, ref seq, $"Add to {playlist.Name}", () => AddToPlaylistAsync(track, playlist));
        }

        if (track.CollectionId.HasValue)
        {
            BuildMenuButton(builder, ref seq, "Open album", () =>
            {
                Nav.NavigateTo(MediaNavigation.ForCollectionMedia(track.MediaType, track.CollectionId.Value, track.Id));
                return Task.CompletedTask;
            });
        }

        builder.CloseElement();
        builder.CloseElement();
    };

    private static void BuildArtwork(RenderTreeBuilder builder, ref int seq, string className, string? url, string alt, string fallbackIcon)
    {
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", className);
        if (!string.IsNullOrWhiteSpace(url))
        {
            builder.OpenElement(seq++, "img");
            builder.AddAttribute(seq++, "src", url);
            builder.AddAttribute(seq++, "alt", alt);
            builder.CloseElement();
        }
        else
        {
            builder.OpenComponent<MudIcon>(seq++);
            builder.AddAttribute(seq++, "Icon", fallbackIcon);
            builder.AddAttribute(seq++, "Style", "font-size: 42px;");
            builder.CloseComponent();
        }
        builder.CloseElement();
    }

    private void BuildTrackTitleCell(RenderTreeBuilder builder, ref int seq, WorkViewModel track, string sourceLabel, IReadOnlyList<WorkViewModel> tracks)
    {
        builder.OpenElement(seq++, "div");
        builder.AddAttribute(seq++, "class", "listen-track-title");
        BuildArtwork(builder, ref seq, "listen-track-title__thumb", track.CoverUrl, track.Title, Icons.Material.Outlined.MusicNote);
        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "listen-track-link");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => PlayTracksAsync(tracks, track.Id, sourceLabel)));
        builder.AddContent(seq++, track.Title);
        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildLinkedCell(RenderTreeBuilder builder, ref int seq, string label, Action action)
    {
        builder.OpenElement(seq++, "td");
        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "listen-inline-link");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, action));
        builder.AddContent(seq++, label);
        builder.CloseElement();
        builder.CloseElement();
    }

    private static void BuildSimpleCell(RenderTreeBuilder builder, ref int seq, string value)
    {
        builder.OpenElement(seq++, "td");
        builder.AddContent(seq++, value);
        builder.CloseElement();
    }

    private void BuildTableHeader(RenderTreeBuilder builder, ref int seq, string label, string? sortKey = null, bool sortable = false)
    {
        builder.OpenElement(seq++, "th");
        if (sortable && !string.IsNullOrWhiteSpace(sortKey))
        {
            var isActive = string.Equals(_songSortColumn, sortKey, StringComparison.OrdinalIgnoreCase);
            var arrow = isActive ? (_songSortDescending ? " ↓" : " ↑") : string.Empty;
            builder.OpenElement(seq++, "button");
            builder.AddAttribute(seq++, "type", "button");
            builder.AddAttribute(seq++, "class", $"listen-table__sort {(isActive ? "is-active" : null)}");
            builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, () => HandleSongSort(sortKey)));
            builder.AddContent(seq++, label + arrow);
            builder.CloseElement();
        }
        else
        {
            builder.AddContent(seq++, label);
        }
        builder.CloseElement();
    }

    private void BuildMenuButton(RenderTreeBuilder builder, ref int seq, string label, Func<Task> action)
    {
        builder.OpenElement(seq++, "button");
        builder.AddAttribute(seq++, "type", "button");
        builder.AddAttribute(seq++, "class", "listen-row-menu__action");
        builder.AddAttribute(seq++, "onclick", EventCallback.Factory.Create(this, action));
        builder.AddContent(seq++, label);
        builder.CloseElement();
    }
#pragma warning restore ASP0006

    private static bool IsMusicWork(WorkViewModel work)
    {
        var mediaType = work.MediaType ?? string.Empty;
        return mediaType.Contains("Music", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mediaType, "Audio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAudiobookWork(WorkViewModel work)
        => (work.MediaType ?? string.Empty).Contains("Audiobook", StringComparison.OrdinalIgnoreCase)
           || string.Equals(work.MediaType, "M4B", StringComparison.OrdinalIgnoreCase);

    private static int ParseTrackNumber(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue;

    private static string GetTrackDuration(WorkViewModel work)
    {
        var duration = work.CanonicalValues.FirstOrDefault(item =>
            string.Equals(item.Key, "duration", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Key, "runtime", StringComparison.OrdinalIgnoreCase))?.Value;

        return string.IsNullOrWhiteSpace(duration) ? "—" : duration;
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "—";

    private static string FormatDateAdded(DateTimeOffset createdAt)
        => createdAt == default ? "—" : createdAt.LocalDateTime.ToString("M/d/yyyy", CultureInfo.CurrentCulture);

    private static int GetPlayCount(WorkViewModel work)
    {
        var value = work.CanonicalValues.FirstOrDefault(item =>
            string.Equals(item.Key, "play_count", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Key, "plays", StringComparison.OrdinalIgnoreCase))?.Value;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plays) ? plays : 0;
    }

    private static string GetPlayCountDisplay(WorkViewModel work)
        => GetPlayCount(work) > 0 ? GetPlayCount(work).ToString(CultureInfo.InvariantCulture) : "—";

    private static string PlaylistIconFor(ManagedCollectionViewModel collection)
        => collection.CollectionType switch
        {
            "Smart" => Icons.Material.Outlined.AutoAwesomeMotion,
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
               || string.Equals(collection.CollectionType, "Mix", StringComparison.OrdinalIgnoreCase)
               || string.Equals(collection.CollectionType, "System", StringComparison.OrdinalIgnoreCase);
    }

    private static string Pluralize(int count, string singular)
        => count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    private static ListenPlaylistCard ToPlaylistCard(ManagedCollectionViewModel playlist)
        => new(
            playlist.Name,
            $"{playlist.TypeLabel} Playlist",
            $"{playlist.ItemCount} items",
            $"/listen/music/playlists/{playlist.Id}",
            playlist.Description ?? "Playlist detail with queue-first listening controls.");

    private sealed record ListenNavItem(string Label, string Route, string Icon, string? Meta = null, bool IsChild = false);
    private sealed record ListenOverviewCard(string Label, string Meta, string Route, string Icon);
    private sealed record ListenPlaylistCard(string Title, string Type, string Meta, string Route, string Description);
}
