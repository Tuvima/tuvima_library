namespace MediaEngine.Web.Services.Playback;

public sealed class ListenAudioDragService
{
    private readonly List<ListenQueueItem> _items = [];
    private readonly HashSet<Guid> _workIds = [];

    public IReadOnlyList<ListenQueueItem> Items => _items;
    public IReadOnlySet<Guid> WorkIds => _workIds;
    public bool HasItems => _items.Count > 0 || _workIds.Count > 0;

    public void BeginDrag(IEnumerable<ListenQueueItem> items)
    {
        _items.Clear();
        _workIds.Clear();

        foreach (var item in items.Where(item => item.WorkId != Guid.Empty))
        {
            _items.Add(item);
            _workIds.Add(item.WorkId);
        }
    }

    public void BeginDrag(IEnumerable<Guid> workIds, IEnumerable<ListenQueueItem>? items = null)
    {
        _items.Clear();
        _workIds.Clear();

        foreach (var workId in workIds.Where(id => id != Guid.Empty))
        {
            _workIds.Add(workId);
        }

        if (items is null)
        {
            return;
        }

        foreach (var item in items.Where(item => item.WorkId != Guid.Empty))
        {
            _items.Add(item);
            _workIds.Add(item.WorkId);
        }
    }

    public void Clear()
    {
        _items.Clear();
        _workIds.Clear();
    }
}
