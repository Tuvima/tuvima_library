using System.Diagnostics.Metrics;

namespace MediaEngine.Api.Services;

/// <summary>
/// Low-cardinality performance instruments shared by interactive request and
/// background-work admission paths. Consumers can attach dotnet-counters or an
/// OpenTelemetry meter provider without changing the hot paths.
/// </summary>
public static class PerformanceMetrics
{
    public const string MeterName = "Tuvima.Library.Performance";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly UpDownCounter<long> ActiveInteractiveRequests =
        Meter.CreateUpDownCounter<long>("tuvima.interactive.requests.active");

    public static readonly Histogram<double> InteractiveRequestDurationMs =
        Meter.CreateHistogram<double>("tuvima.interactive.request.duration", "ms");

    public static readonly Histogram<double> DatabaseReadDurationMs =
        Meter.CreateHistogram<double>("tuvima.database.read.duration", "ms");

    public static readonly Counter<long> BackgroundAiAdmissions =
        Meter.CreateCounter<long>("tuvima.ai.background.admissions");

    public static readonly Counter<long> BackgroundAiDeferrals =
        Meter.CreateCounter<long>("tuvima.ai.background.deferrals");

    public static readonly Counter<long> BackgroundAiPreemptions =
        Meter.CreateCounter<long>("tuvima.ai.background.preemptions");
}
