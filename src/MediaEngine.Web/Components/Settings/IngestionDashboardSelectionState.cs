using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;

namespace MediaEngine.Web.Components.Settings;

/// <summary>
/// Keeps batch and stage selection stable as live ingestion snapshots replace or
/// reorder rows.
/// </summary>
public sealed class IngestionDashboardSelectionState
{
    public Guid? BatchId { get; private set; }
    public string? StageKey { get; private set; }
    public string Signature => $"{BatchId:N}:{StageKey}";

    public void Synchronize(
        IReadOnlyList<IngestionOperationsBatchViewModel> batches,
        IReadOnlyList<IngestionDashboardStage> stages,
        Func<IngestionDashboardStage, string> stageKey,
        Func<IReadOnlyList<IngestionDashboardStage>, IngestionDashboardStage?> defaultStage)
    {
        if (BatchId is null && batches.FirstOrDefault() is { } latestBatch)
            BatchId = latestBatch.BatchId;

        if (stages.Count == 0)
        {
            StageKey = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(StageKey)
            || stages.All(stage => !stageKey(stage).Equals(StageKey, StringComparison.OrdinalIgnoreCase)))
        {
            StageKey = stageKey(defaultStage(stages) ?? stages[0]);
        }
    }

    public void SelectStage(string stageKey) => StageKey = stageKey;

    public void SelectBatch(Guid batchId)
    {
        BatchId = batchId;
        StageKey = null;
    }

    public IngestionOperationsBatchViewModel? ResolveBatch(
        IReadOnlyList<IngestionOperationsBatchViewModel> batches)
    {
        if (BatchId is { } selectedId)
        {
            var selected = batches.FirstOrDefault(batch => batch.BatchId == selectedId);
            if (selected is not null)
                return selected;
        }

        return batches.FirstOrDefault();
    }
}
