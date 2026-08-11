using MediaEngine.Domain.Configuration;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Supplies atomic configuration snapshots to pipeline workers. A single worker
/// operation should capture <see cref="Current"/> once and use that revision
/// throughout the operation.
/// </summary>
public interface IPipelineExecutionSnapshotProvider
{
    PipelineExecutionSnapshot Current { get; }
}
