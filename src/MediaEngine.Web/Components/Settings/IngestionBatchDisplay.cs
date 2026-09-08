using MediaEngine.Contracts.Activity;

namespace MediaEngine.Web.Components.Settings;

public static class IngestionBatchDisplay
{
    public static bool HasMedia(ActivityBatchSummaryDto batch) => batch.AddedGroupCount > 0;

    public static string Title(ActivityBatchSummaryDto batch)
    {
        if (!string.IsNullOrWhiteSpace(batch.Category))
            return batch.Category.EndsWith("import", StringComparison.OrdinalIgnoreCase)
                ? batch.Category
                : $"{batch.Category} import";

        if (batch.MediaTypeCount > 1)
            return "Mixed media scan";

        return batch.MediaTypes.Count == 1
            ? $"{LaneFor(batch.MediaTypes[0].MediaType)} import"
            : "Library scan";
    }

    public static string FilesChecked(ActivityBatchSummaryDto batch)
    {
        var processed = Math.Max(0, batch.FilesProcessedCount);
        var discovered = Math.Max(0, batch.FilesDiscoveredCount);
        return discovered > 0 && processed != discovered
            ? $"{processed:N0} of {discovered:N0} files checked"
            : $"{processed:N0} {Pluralize("file", processed)} checked";
    }

    public static string AddedOutcome(ActivityBatchSummaryDto batch) =>
        HasMedia(batch)
            ? $"{batch.AddedGroupCount:N0} library {Pluralize("item", batch.AddedGroupCount)} added"
            : "No new media added";

    public static string Summary(ActivityBatchSummaryDto batch)
    {
        if (batch.FailureCount > 0)
            return HasMedia(batch)
                ? "Ingestion completed with failures."
                : "The scan completed with failures and added no media.";

        if (batch.ReviewCount > 0)
            return HasMedia(batch)
                ? "Ingestion completed; some items need follow-up."
                : "The scan found items that need review before they can be added.";

        return HasMedia(batch)
            ? "Ingestion completed successfully."
            : "The scan completed and found no new media to add.";
    }

    public static string MediaHref(ActivityBatchSummaryDto batch) =>
        $"/settings/ingestion?runId={batch.BatchId:D}&view=all";

    private static string LaneFor(string media) => media.ToLowerInvariant() switch
    {
        var value when value.Contains("book") || value.Contains("comic") => "Read",
        var value when value.Contains("movie") || value.Contains("tv") => "Watch",
        var value when value.Contains("music") || value.Contains("audio") => "Listen",
        _ => "Media",
    };

    private static string Pluralize(string word, int count) => count == 1 ? word : $"{word}s";
}
