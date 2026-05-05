using System.Collections.Concurrent;

namespace MediaEngine.Api.Services.Plugins;

public sealed class PluginJobStateService
{
    private readonly ConcurrentDictionary<string, PluginJobSnapshot> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PluginJobSnapshot> List(string? pluginId = null)
    {
        var jobs = _jobs.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(pluginId))
            jobs = jobs.Where(j => string.Equals(j.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));

        return jobs.OrderByDescending(j => j.StartedAt).ToList();
    }

    public PluginJobSnapshot Start(string pluginId, string jobType)
    {
        var job = new PluginJobSnapshot
        {
            Id = Guid.NewGuid(),
            PluginId = pluginId,
            JobType = jobType,
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
        };
        _jobs[job.Id.ToString("N")] = job;
        return job;
    }

    public void Complete(Guid id, int assetsScanned, int segmentsWritten)
    {
        Update(id, job =>
        {
            job.Status = "completed";
            job.AssetsScanned = assetsScanned;
            job.SegmentsWritten = segmentsWritten;
            job.CompletedAt = DateTimeOffset.UtcNow;
        });
    }

    public void Fail(Guid id, string error, int assetsScanned = 0, int segmentsWritten = 0)
    {
        Update(id, job =>
        {
            job.Status = "failed";
            job.Error = error;
            job.AssetsScanned = assetsScanned;
            job.SegmentsWritten = segmentsWritten;
            job.CompletedAt = DateTimeOffset.UtcNow;
        });
    }

    private void Update(Guid id, Action<PluginJobSnapshot> update)
    {
        var key = id.ToString("N");
        if (!_jobs.TryGetValue(key, out var job))
            return;

        update(job);
        _jobs[key] = job;
    }
}

public sealed class PluginJobSnapshot
{
    public Guid Id { get; init; }
    public string PluginId { get; init; } = "";
    public string JobType { get; init; } = "";
    public string Status { get; set; } = "queued";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int AssetsScanned { get; set; }
    public int SegmentsWritten { get; set; }
    public string? Error { get; set; }
}
