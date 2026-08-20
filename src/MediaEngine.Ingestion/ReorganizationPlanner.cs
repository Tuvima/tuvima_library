using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion;

/// <summary>
/// Produces a deterministic, side-effect-free reorganization preview. The
/// returned aggregate is suitable for durable persistence, but this planner
/// deliberately performs no filesystem writes.
/// </summary>
public sealed class ReorganizationPlanner : IReorganizationPlanner
{
    private readonly ISourceMutationPolicyGate _mutationGate;

    public ReorganizationPlanner(ISourceMutationPolicyGate mutationGate)
    {
        _mutationGate = mutationGate;
    }

    public ReorganizationPlan CreatePlan(ReorganizationPlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LibraryId);

        var sources = BuildSourceIndex(request.Sources);
        var overlappingSources = FindOverlappingSources(request.Sources);
        var existingPaths = NormalizeExistingPaths(request.ExistingPaths);
        var proposedPaths = new HashSet<string>(PathSafety.Comparer);
        var remainingBytes = new Dictionary<string, long>(
            request.AvailableBytesByDestinationSource,
            StringComparer.OrdinalIgnoreCase);
        var operations = new List<ReorganizationPlanOperation>(request.Candidates.Count);

        for (int index = 0; index < request.Candidates.Count; index++)
        {
            var candidate = request.Candidates[index];
            operations.Add(EvaluateCandidate(
                index,
                request.LibraryId,
                candidate,
                sources,
                overlappingSources,
                existingPaths,
                proposedPaths,
                remainingBytes));
        }

        var summary = Summarize(operations);
        return new ReorganizationPlan
        {
            Id = request.PlanId,
            LibraryId = request.LibraryId,
            CreatedAt = request.CreatedAt,
            Operations = operations,
            Summary = summary,
            Fingerprint = ReorganizationPlan.CalculateFingerprint(request.PlanId, request.LibraryId, operations),
        };
    }

    private ReorganizationPlanOperation EvaluateCandidate(
        int sequence,
        string libraryId,
        ReorganizationCandidate candidate,
        IReadOnlyDictionary<string, FileSourceMutationPolicy> sources,
        IReadOnlySet<string> overlappingSources,
        IReadOnlySet<string> existingPaths,
        HashSet<string> proposedPaths,
        IDictionary<string, long> remainingBytes)
    {
        ReorganizationPlanOperation Result(ReorganizationDisposition disposition, string? reason = null, string? current = null, string? proposed = null)
            => new()
            {
                Sequence = sequence,
                SourceId = candidate.SourceId,
                DestinationSourceId = candidate.DestinationSourceId,
                CurrentPath = current ?? candidate.CurrentPath,
                ProposedPath = proposed ?? candidate.ProposedPath,
                Disposition = disposition,
                SizeBytes = candidate.SizeBytes,
                Reason = reason,
            };

        if (!string.IsNullOrWhiteSpace(candidate.Error))
            return Result(ReorganizationDisposition.Error, candidate.Error);

        if (candidate.SizeBytes < 0)
            return Result(ReorganizationDisposition.Error, "File size cannot be negative.");

        if (!sources.TryGetValue(candidate.SourceId, out var source))
            return Result(ReorganizationDisposition.Error, "The current source is not part of the plan.");

        if (!sources.TryGetValue(candidate.DestinationSourceId, out var destination))
            return Result(ReorganizationDisposition.Error, "The destination source is not part of the plan.");

        if (!string.Equals(source.LibraryId, libraryId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(destination.LibraryId, libraryId, StringComparison.OrdinalIgnoreCase))
        {
            return Result(ReorganizationDisposition.Blocked, "Cross-library reorganization is not permitted.");
        }

        if (!PathSafety.TryNormalizeRoot(source.RootPath, out _, out var sourceRootError))
            return Result(ReorganizationDisposition.Error, sourceRootError);

        if (!PathSafety.TryNormalizeRoot(destination.RootPath, out _, out var destinationRootError))
            return Result(ReorganizationDisposition.Error, destinationRootError);

        if (!PathSafety.TryNormalizePath(candidate.CurrentPath, out var currentPath, out var currentError))
            return Result(ReorganizationDisposition.Error, currentError);

        if (!string.IsNullOrWhiteSpace(candidate.UnresolvedReason) || string.IsNullOrWhiteSpace(candidate.ProposedPath))
            return Result(ReorganizationDisposition.Unresolved, candidate.UnresolvedReason ?? "No destination path could be resolved.", current: currentPath);

        if (!PathSafety.TryNormalizePath(candidate.ProposedPath, out var proposedPath, out var proposedError))
            return Result(ReorganizationDisposition.Error, proposedError, current: currentPath);

        if (overlappingSources.Contains(source.SourceId) || overlappingSources.Contains(destination.SourceId))
            return Result(ReorganizationDisposition.Blocked, "Configured source roots overlap and cannot be reorganized safely.", currentPath, proposedPath);

        if (PathSafety.Comparer.Equals(currentPath, proposedPath))
            return Result(ReorganizationDisposition.Unchanged, current: currentPath, proposed: proposedPath);

        bool sameParent = PathSafety.Comparer.Equals(
            Path.GetDirectoryName(currentPath) ?? string.Empty,
            Path.GetDirectoryName(proposedPath) ?? string.Empty);
        var mutation = sameParent ? SourceMutationKind.Rename : SourceMutationKind.Move;

        var sourceDecision = _mutationGate.Evaluate(new SourceMutationRequest
        {
            Source = source,
            Mutation = mutation,
            Path = currentPath!,
        });
        if (!sourceDecision.Allowed)
            return Result(ReorganizationDisposition.Blocked, sourceDecision.Reason, currentPath, proposedPath);

        var destinationDecision = _mutationGate.Evaluate(new SourceMutationRequest
        {
            Source = destination,
            Mutation = SourceMutationKind.UseAsDestination,
            Path = proposedPath!,
        });
        if (!destinationDecision.Allowed)
            return Result(ReorganizationDisposition.Blocked, destinationDecision.Reason, currentPath, proposedPath);

        bool collidesWithExisting = existingPaths.Contains(proposedPath!)
            && !PathSafety.Comparer.Equals(currentPath, proposedPath);
        if (collidesWithExisting || proposedPaths.Contains(proposedPath!))
            return Result(ReorganizationDisposition.Conflict, "The proposed destination already exists or is used by another planned operation.", currentPath, proposedPath);

        var disposition = sameParent
            ? ReorganizationDisposition.Renamed
            : ReorganizationDisposition.Moved;

        // Renames within one directory need no additional destination capacity.
        if (disposition == ReorganizationDisposition.Moved
            && !string.Equals(source.SourceId, destination.SourceId, StringComparison.OrdinalIgnoreCase)
            && remainingBytes.TryGetValue(destination.SourceId, out long availableBytes))
        {
            if (candidate.SizeBytes > availableBytes)
                return Result(ReorganizationDisposition.Blocked, "The destination does not have enough available disk space.", currentPath, proposedPath);

            remainingBytes[destination.SourceId] = availableBytes - candidate.SizeBytes;
        }

        proposedPaths.Add(proposedPath!);
        return Result(disposition, current: currentPath, proposed: proposedPath);
    }

    private static IReadOnlyDictionary<string, FileSourceMutationPolicy> BuildSourceIndex(
        IReadOnlyList<FileSourceMutationPolicy> sources)
    {
        var result = new Dictionary<string, FileSourceMutationPolicy>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);
            if (!result.TryAdd(source.SourceId, source))
                throw new ArgumentException($"Duplicate source identity '{source.SourceId}' is not allowed.", nameof(sources));
        }

        return result;
    }

    private static HashSet<string> NormalizeExistingPaths(IReadOnlySet<string> existingPaths)
    {
        var result = new HashSet<string>(PathSafety.Comparer);
        foreach (var path in existingPaths)
        {
            if (PathSafety.TryNormalizePath(path, out var normalized, out _))
                result.Add(normalized!);
        }

        return result;
    }

    private static HashSet<string> FindOverlappingSources(IReadOnlyList<FileSourceMutationPolicy> sources)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<(string Id, string Root)>();

        foreach (var source in sources)
        {
            if (PathSafety.TryNormalizeRoot(source.RootPath, out var root, out _))
                normalized.Add((source.SourceId, root!));
        }

        for (int first = 0; first < normalized.Count; first++)
        {
            for (int second = first + 1; second < normalized.Count; second++)
            {
                if (string.Equals(normalized[first].Id, normalized[second].Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (PathSafety.Overlaps(normalized[first].Root, normalized[second].Root))
                {
                    result.Add(normalized[first].Id);
                    result.Add(normalized[second].Id);
                }
            }
        }

        return result;
    }

    private static ReorganizationPlanSummary Summarize(IReadOnlyList<ReorganizationPlanOperation> operations) => new()
    {
        Total = operations.Count,
        Unchanged = operations.Count(static item => item.Disposition == ReorganizationDisposition.Unchanged),
        Renamed = operations.Count(static item => item.Disposition == ReorganizationDisposition.Renamed),
        Moved = operations.Count(static item => item.Disposition == ReorganizationDisposition.Moved),
        Conflicts = operations.Count(static item => item.Disposition == ReorganizationDisposition.Conflict),
        Unresolved = operations.Count(static item => item.Disposition == ReorganizationDisposition.Unresolved),
        Blocked = operations.Count(static item => item.Disposition == ReorganizationDisposition.Blocked),
        Errors = operations.Count(static item => item.Disposition == ReorganizationDisposition.Error),
    };
}
