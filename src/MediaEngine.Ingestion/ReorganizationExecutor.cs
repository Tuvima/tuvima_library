using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Executes an explicitly confirmed plan without overwrite or rollback. Every
/// operation re-resolves authoritative source policies immediately before its
/// mutation and reports an independent outcome so partial failure is visible.
/// </summary>
public sealed class ReorganizationExecutor(
    ISourceMutationPolicyGate mutationGate,
    IReorganizationFileSystem fileSystem) : IReorganizationExecutor
{
    public ReorganizationExecutionResult Execute(
        ReorganizationPlan confirmedPlan,
        IReadOnlyDictionary<string, FileSourceMutationPolicy> plannedPolicies,
        Func<IReadOnlyList<FileSourceMutationPolicy>> currentPoliciesResolver,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(confirmedPlan);
        ArgumentNullException.ThrowIfNull(plannedPolicies);
        ArgumentNullException.ThrowIfNull(currentPoliciesResolver);

        var operations = confirmedPlan.GetConfirmedOperations();
        var results = new List<ReorganizationExecutionItemResult>(operations.Count);
        foreach (var operation in operations.OrderBy(item => item.Sequence))
        {
            ct.ThrowIfCancellationRequested();
            results.Add(ExecuteOne(
                confirmedPlan.LibraryId,
                operation,
                plannedPolicies,
                currentPoliciesResolver));
        }

        return new ReorganizationExecutionResult
        {
            PlanId = confirmedPlan.Id,
            LibraryId = confirmedPlan.LibraryId,
            Fingerprint = confirmedPlan.Fingerprint,
            Items = results,
        };
    }

    private ReorganizationExecutionItemResult ExecuteOne(
        string libraryId,
        ReorganizationPlanOperation operation,
        IReadOnlyDictionary<string, FileSourceMutationPolicy> plannedPolicies,
        Func<IReadOnlyList<FileSourceMutationPolicy>> currentPoliciesResolver)
    {
        ReorganizationExecutionItemResult Result(
            ReorganizationExecutionDisposition disposition,
            string? reason = null) => new()
        {
            Sequence = operation.Sequence,
            CurrentPath = operation.CurrentPath,
            ProposedPath = operation.ProposedPath ?? string.Empty,
            Disposition = disposition,
            Reason = reason,
        };

        try
        {
            if (string.IsNullOrWhiteSpace(operation.ProposedPath))
                return Result(ReorganizationExecutionDisposition.Blocked, "The destination path is unresolved.");

            var currentPolicies = currentPoliciesResolver();
            var currentById = new Dictionary<string, FileSourceMutationPolicy>(StringComparer.OrdinalIgnoreCase);
            foreach (var policy in currentPolicies)
            {
                if (!currentById.TryAdd(policy.SourceId, policy))
                    return Result(ReorganizationExecutionDisposition.Blocked, "Current source identities are not unique.");
            }

            if (!TryResolveUnchangedPolicy(
                    operation.SourceId,
                    libraryId,
                    plannedPolicies,
                    currentById,
                    out var source,
                    out var sourceError))
            {
                return Result(ReorganizationExecutionDisposition.Blocked, sourceError);
            }
            if (!TryResolveUnchangedPolicy(
                    operation.DestinationSourceId,
                    libraryId,
                    plannedPolicies,
                    currentById,
                    out var destination,
                    out var destinationError))
            {
                return Result(ReorganizationExecutionDisposition.Blocked, destinationError);
            }

            if (HasOverlappingSources(currentPolicies))
                return Result(ReorganizationExecutionDisposition.Blocked, "Current source roots overlap.");

            if (!PathSafety.TryNormalizePath(operation.CurrentPath, out var currentPath, out var currentError))
                return Result(ReorganizationExecutionDisposition.Blocked, currentError);
            if (!PathSafety.TryNormalizePath(operation.ProposedPath, out var proposedPath, out var proposedError))
                return Result(ReorganizationExecutionDisposition.Blocked, proposedError);
            if (PathSafety.Comparer.Equals(currentPath, proposedPath))
                return Result(ReorganizationExecutionDisposition.Blocked, "The proposed move no longer changes the path.");

            var sameParent = PathSafety.Comparer.Equals(
                Path.GetDirectoryName(currentPath),
                Path.GetDirectoryName(proposedPath));
            var sourceDecision = mutationGate.Evaluate(new SourceMutationRequest
            {
                Source = source!,
                Mutation = sameParent ? SourceMutationKind.Rename : SourceMutationKind.Move,
                Path = currentPath!,
            });
            if (!sourceDecision.Allowed)
                return Result(ReorganizationExecutionDisposition.Blocked, sourceDecision.Reason);

            var destinationDecision = mutationGate.Evaluate(new SourceMutationRequest
            {
                Source = destination!,
                Mutation = SourceMutationKind.UseAsDestination,
                Path = proposedPath!,
            });
            if (!destinationDecision.Allowed)
                return Result(ReorganizationExecutionDisposition.Blocked, destinationDecision.Reason);

            if (!fileSystem.FileExists(currentPath!))
                return Result(ReorganizationExecutionDisposition.Blocked, "The current file no longer exists.");
            if (fileSystem.GetFileLength(currentPath!) != operation.SizeBytes)
                return Result(ReorganizationExecutionDisposition.Blocked, "The current file changed after preview.");
            if (fileSystem.FileExists(proposedPath!) || fileSystem.DirectoryExists(proposedPath!))
                return Result(ReorganizationExecutionDisposition.Blocked, "The destination now exists.");

            if (!string.Equals(operation.SourceId, operation.DestinationSourceId, StringComparison.OrdinalIgnoreCase)
                && fileSystem.GetAvailableBytes(proposedPath!) < operation.SizeBytes)
            {
                return Result(ReorganizationExecutionDisposition.Blocked, "The destination no longer has enough disk space.");
            }

            var destinationDirectory = Path.GetDirectoryName(proposedPath!);
            if (string.IsNullOrWhiteSpace(destinationDirectory))
                return Result(ReorganizationExecutionDisposition.Blocked, "The destination directory is unresolved.");

            fileSystem.CreateDirectory(destinationDirectory);
            // No overwrite is permitted. A race that creates the destination
            // after the check fails this operation without replacing it.
            fileSystem.MoveFile(currentPath!, proposedPath!);
            return Result(ReorganizationExecutionDisposition.Moved);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Result(ReorganizationExecutionDisposition.Failed, exception.Message);
        }
    }

    private static bool TryResolveUnchangedPolicy(
        string sourceId,
        string libraryId,
        IReadOnlyDictionary<string, FileSourceMutationPolicy> plannedPolicies,
        IReadOnlyDictionary<string, FileSourceMutationPolicy> currentPolicies,
        out FileSourceMutationPolicy? policy,
        out string? error)
    {
        policy = null;
        error = null;
        if (!plannedPolicies.TryGetValue(sourceId, out var planned))
        {
            error = $"Source '{sourceId}' was not captured by the confirmed plan.";
            return false;
        }
        if (!currentPolicies.TryGetValue(sourceId, out var current))
        {
            error = $"Source '{sourceId}' no longer exists.";
            return false;
        }
        if (!string.Equals(current.LibraryId, libraryId, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Source '{sourceId}' no longer belongs to this library.";
            return false;
        }
        if (current != planned)
        {
            error = $"Source policy '{sourceId}' changed after preview.";
            return false;
        }

        policy = current;
        return true;
    }

    private static bool HasOverlappingSources(IReadOnlyList<FileSourceMutationPolicy> policies)
    {
        var roots = policies
            .Select(policy => PathSafety.TryNormalizeRoot(policy.RootPath, out var root, out _)
                ? (policy.SourceId, Root: root)
                : (policy.SourceId, Root: null))
            .ToList();
        if (roots.Any(entry => entry.Root is null)) return true;

        for (var left = 0; left < roots.Count; left++)
        {
            for (var right = left + 1; right < roots.Count; right++)
            {
                if (PathSafety.Overlaps(roots[left].Root!, roots[right].Root!)) return true;
            }
        }

        return false;
    }
}
