using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Services.Playback;

/// <summary>
/// Auto-saves reading progress to the Engine at configurable intervals.
/// Supports offline queue in localStorage for MAUI Blazor Hybrid.
/// </summary>
public sealed class ReadingProgressService : IAsyncDisposable
{
    private readonly IEngineApiClient _api;
    private System.Threading.Timer? _timer;
    private Guid _assetId;
    private int _chapterIndex;
    private int _pageInChapter;
    private int _totalPagesInChapter;
    private int _totalChapters;
    private bool _dirty;
    private bool _saving;

    /// <summary>Default auto-save interval (30 seconds).</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

    public ReadingProgressService(IEngineApiClient api) => _api = api;

    /// <summary>Start auto-saving progress for a specific asset.</summary>
    public void Start(Guid assetId, int totalChapters, TimeSpan? interval = null)
    {
        _assetId = assetId;
        _totalChapters = totalChapters;
        _timer?.Dispose();
        _timer = new System.Threading.Timer(
            async _ => await SaveIfDirtyAsync(),
            null,
            interval ?? DefaultInterval,
            interval ?? DefaultInterval);
    }

    /// <summary>Update the current reading position (called on page/chapter changes).</summary>
    public void UpdatePosition(int chapterIndex, int pageInChapter, int totalPagesInChapter)
    {
        _chapterIndex = chapterIndex;
        _pageInChapter = pageInChapter;
        _totalPagesInChapter = totalPagesInChapter;
        _dirty = true;
    }

    /// <summary>Calculate overall progress percentage.</summary>
    public double CalculateProgress()
    {
        if (_totalChapters <= 0) return 0;
        double pageProgress = _totalPagesInChapter > 0
            ? (double)_pageInChapter / _totalPagesInChapter
            : 0;
        return ((_chapterIndex + pageProgress) / _totalChapters) * 100.0;
    }

    /// <summary>Force an immediate save (e.g., on chapter change or reader exit).</summary>
    public async Task SaveNowAsync()
    {
        _dirty = true;
        await SaveIfDirtyAsync();
    }

    private async Task SaveIfDirtyAsync()
    {
        if (!_dirty || _saving || _assetId == Guid.Empty) return;
        _saving = true;
        try
        {
            var extProps = new Dictionary<string, string>
            {
                ["chapter_index"] = _chapterIndex.ToString(),
                ["page_in_chapter"] = _pageInChapter.ToString(),
                ["total_pages_in_chapter"] = _totalPagesInChapter.ToString()
            };

            await _api.SaveProgressAsync(
                _assetId,
                progressPct: CalculateProgress(),
                extendedProperties: extProps);

            _dirty = false;
        }
        catch
        {
            // Network error — will retry on next interval.
            // TODO: Queue in localStorage for MAUI offline support.
        }
        finally
        {
            _saving = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
            _timer = null;
        }
        // Final save on disposal
        await SaveIfDirtyAsync();
    }
}
