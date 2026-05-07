namespace MediaEngine.Web.Services.Playback;

public sealed class ListenPageState
{
    public string ActiveMode { get; private set; } = "music";
    public string Search { get; private set; } = string.Empty;
    public string SortColumn { get; private set; } = "dateAdded";
    public bool SortDescending { get; private set; } = true;
    public Guid? SelectedCollectionId { get; private set; }
    public string? SelectedArtistName { get; private set; }
    public bool Loading { get; private set; } = true;
    public string? Error { get; private set; }
    public IReadOnlySet<Guid> SelectedTrackIds => _selectedTrackIds;

    private readonly HashSet<Guid> _selectedTrackIds = [];

    public void SetMode(string mode)
    {
        ActiveMode = string.Equals(mode, "audiobooks", StringComparison.OrdinalIgnoreCase) ? "audiobooks" : "music";
    }

    public void SetSearch(string? search) => Search = search ?? string.Empty;

    public void SetSort(string column, bool descending)
    {
        SortColumn = string.IsNullOrWhiteSpace(column) ? "dateAdded" : column;
        SortDescending = descending;
    }

    public void SelectCollection(Guid? collectionId) => SelectedCollectionId = collectionId;

    public void SelectArtist(string? artistName) => SelectedArtistName = string.IsNullOrWhiteSpace(artistName) ? null : artistName;

    public void SetLoading(bool loading) => Loading = loading;

    public void SetError(string? error)
    {
        Error = string.IsNullOrWhiteSpace(error) ? null : error;
        Loading = false;
    }

    public void ReplaceTrackSelection(IEnumerable<Guid> trackIds)
    {
        _selectedTrackIds.Clear();
        foreach (var id in trackIds)
        {
            _selectedTrackIds.Add(id);
        }
    }

    public void ClearTrackSelection() => _selectedTrackIds.Clear();
}
