using System.Text.Json.Serialization;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Services.Playback;

public sealed class ListenPlaybackService
{
    private readonly UIOrchestratorService _orchestrator;
    private readonly IEngineApiClient _apiClient;
    private readonly List<ListenQueueItem> _queue = [];

    public ListenPlaybackService(UIOrchestratorService orchestrator, IEngineApiClient apiClient)
    {
        _orchestrator = orchestrator;
        _apiClient = apiClient;
    }

    public event Action? OnChanged;

    public IReadOnlyList<ListenQueueItem> Queue => _queue;
    public int CurrentIndex { get; private set; } = -1;
    public string? SourceLabel { get; private set; }
    public bool IsQueueOpen { get; private set; }

    public bool HasQueue => _queue.Count > 0;

    public ListenQueueItem? CurrentItem =>
        CurrentIndex >= 0 && CurrentIndex < _queue.Count
            ? _queue[CurrentIndex]
            : null;

    public string? CurrentStreamUrl => CurrentItem?.StreamUrl;

    public async Task PlayWorkAsync(WorkViewModel work, string? sourceLabel = null, CancellationToken ct = default)
    {
        await ReplaceQueueAsync([work], 0, sourceLabel ?? work.Album ?? work.Title, false, ct);
    }

    public async Task ReplaceQueueAsync(
        IEnumerable<WorkViewModel> works,
        int startIndex,
        string? sourceLabel,
        bool shuffle,
        CancellationToken ct = default)
    {
        var items = works.Select(CreateQueueItem).ToList();
        if (items.Count == 0)
        {
            _queue.Clear();
            CurrentIndex = -1;
            SourceLabel = null;
            NotifyChanged();
            return;
        }

        if (shuffle)
        {
            var random = new Random();
            items = items.OrderBy(_ => random.Next()).ToList();
            startIndex = 0;
        }

        _queue.Clear();
        _queue.AddRange(items);
        CurrentIndex = Math.Clamp(startIndex, 0, _queue.Count - 1);
        SourceLabel = sourceLabel;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
    }

    public async Task InsertNextAsync(WorkViewModel work, CancellationToken ct = default)
    {
        var item = CreateQueueItem(work);
        if (_queue.Count == 0)
        {
            _queue.Add(item);
            CurrentIndex = 0;
            SourceLabel = work.Album ?? work.Title;
            await EnsurePlayableAsync(CurrentIndex, ct);
            NotifyChanged();
            return;
        }

        var insertIndex = Math.Clamp(CurrentIndex + 1, 0, _queue.Count);
        _queue.Insert(insertIndex, item);
        NotifyChanged();
    }

    public async Task PlayIndexAsync(int index, CancellationToken ct = default)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        CurrentIndex = index;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
    }

    public async Task SkipNextAsync(CancellationToken ct = default)
    {
        if (CurrentIndex + 1 >= _queue.Count)
        {
            return;
        }

        CurrentIndex++;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
    }

    public async Task SkipPreviousAsync(CancellationToken ct = default)
    {
        if (CurrentIndex <= 0)
        {
            return;
        }

        CurrentIndex--;
        await EnsurePlayableAsync(CurrentIndex, ct);
        NotifyChanged();
    }

    public void ToggleQueue()
    {
        IsQueueOpen = !IsQueueOpen;
        NotifyChanged();
    }

    public void CloseQueue()
    {
        if (!IsQueueOpen)
        {
            return;
        }

        IsQueueOpen = false;
        NotifyChanged();
    }

    public void RestoreState(ListenPlaybackSnapshot snapshot)
    {
        _queue.Clear();
        _queue.AddRange(snapshot.Queue ?? []);
        CurrentIndex = _queue.Count == 0
            ? -1
            : Math.Clamp(snapshot.CurrentIndex, 0, _queue.Count - 1);
        SourceLabel = snapshot.SourceLabel;
        IsQueueOpen = false;
        NotifyChanged();
    }

    public ListenPlaybackSnapshot CreateSnapshot() => new()
    {
        Queue = _queue.ToList(),
        CurrentIndex = CurrentIndex,
        SourceLabel = SourceLabel,
    };

    private async Task EnsurePlayableAsync(int index, CancellationToken ct)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        var item = _queue[index];
        if (item.AssetId.HasValue && !string.IsNullOrWhiteSpace(item.StreamUrl))
        {
            return;
        }

        var assetId = await _orchestrator.ResolveWorkToAssetAsync(item.WorkId, ct);
        if (!assetId.HasValue)
        {
            return;
        }

        _queue[index] = item with
        {
            AssetId = assetId,
            StreamUrl = _apiClient.ToAbsoluteEngineUrl($"/stream/{assetId.Value}"),
        };
    }

    private static ListenQueueItem CreateQueueItem(WorkViewModel work)
        => new()
        {
            WorkId = work.Id,
            CollectionId = work.CollectionId,
            MediaType = work.MediaType,
            Title = work.Title,
            Subtitle = work.Artist ?? work.Author,
            Album = work.Album ?? work.Series,
            CoverUrl = work.CoverUrl,
            Duration = GetDuration(work),
        };

    private static string? GetDuration(WorkViewModel work)
        => work.CanonicalValues.FirstOrDefault(cv =>
               string.Equals(cv.Key, "duration", StringComparison.OrdinalIgnoreCase)
               || string.Equals(cv.Key, "runtime", StringComparison.OrdinalIgnoreCase))?.Value;

    private void NotifyChanged() => OnChanged?.Invoke();
}

public sealed record ListenQueueItem
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }

    [JsonPropertyName("collection_id")]
    public Guid? CollectionId { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("album")]
    public string? Album { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("asset_id")]
    public Guid? AssetId { get; init; }

    [JsonPropertyName("stream_url")]
    public string? StreamUrl { get; init; }
}

public sealed record ListenPlaybackSnapshot
{
    [JsonPropertyName("queue")]
    public List<ListenQueueItem> Queue { get; init; } = [];

    [JsonPropertyName("current_index")]
    public int CurrentIndex { get; init; }

    [JsonPropertyName("source_label")]
    public string? SourceLabel { get; init; }
}
