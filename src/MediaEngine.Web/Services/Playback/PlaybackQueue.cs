using MediaEngine.Contracts.Playback;

namespace MediaEngine.Web.Services.Playback;

public sealed class PlaybackQueue
{
    private readonly List<ListenQueueItem> _items = [];
    private IReadOnlyList<ListenQueueItem> _upcoming = [];

    public IReadOnlyList<ListenQueueItem> Items => _items;
    public IReadOnlyList<ListenQueueItem> Upcoming => _upcoming;
    public int CurrentIndex { get; private set; } = -1;

    public ListenQueueItem? Current =>
        CurrentIndex >= 0 && CurrentIndex < _items.Count
            ? _items[CurrentIndex]
            : null;

    public void Clear()
    {
        _items.Clear();
        CurrentIndex = -1;
        RefreshUpcoming();
    }

    public void Replace(IEnumerable<ListenQueueItem> items, int startIndex)
    {
        _items.Clear();
        _items.AddRange(items);
        CurrentIndex = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
        RefreshUpcoming();
    }

    /// <summary>
    /// Audiobooks intentionally use a single-item queue. This mirrors Audible and Plex-style long-form audio:
    /// picking another audiobook replaces the current queue instead of appending a multi-book playlist.
    /// </summary>
    public void ReplaceWithSingleAudiobook(ListenQueueItem item)
    {
        Replace([item with { MediaType = "Audiobooks", AudiobookStartKind = AudiobookStartKinds.Resume }], 0);
    }

    public void Add(ListenQueueItem item)
    {
        _items.Add(item);
        if (CurrentIndex < 0)
        {
            CurrentIndex = 0;
        }

        RefreshUpcoming();
    }

    public void InsertNext(ListenQueueItem item)
    {
        var insertIndex = Math.Clamp(CurrentIndex + 1, 0, _items.Count);
        _items.Insert(insertIndex, item);
        RefreshUpcoming();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        _items.RemoveAt(index);
        if (_items.Count == 0)
        {
            CurrentIndex = -1;
        }
        else if (CurrentIndex >= _items.Count)
        {
            CurrentIndex = _items.Count - 1;
        }

        RefreshUpcoming();
    }

    public void ClearUpcoming()
    {
        if (CurrentIndex < 0 || CurrentIndex >= _items.Count - 1)
        {
            return;
        }

        _items.RemoveRange(CurrentIndex + 1, _items.Count - CurrentIndex - 1);
        RefreshUpcoming();
    }

    public bool MoveTo(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        CurrentIndex = index;
        RefreshUpcoming();
        return true;
    }

    private void RefreshUpcoming()
    {
        _upcoming = CurrentIndex < 0 || CurrentIndex >= _items.Count
            ? _items.ToList()
            : _items.Skip(CurrentIndex + 1).ToList();
    }
}
