using System.Collections.Concurrent;
using MediaEngine.Contracts.Ingestion;
using MediaEngine.Domain.Configuration;
using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Ingestion.Services;

namespace MediaEngine.Api.Services.Libraries;

/// <summary>
/// Holds short-lived dry-run plans server-side so execution needs only the
/// exact plan identity and fingerprint. Plans are single-use and do not survive
/// a process restart; either condition requires a fresh safety preview.
/// </summary>
public sealed class LibraryReorganizationService(
    IConfigurationLoader configuration,
    IReorganizationPlanner planner,
    IReorganizationExecutor executor,
    IReorganizationFileSystem fileSystem)
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<Guid, PendingPlan> _plans = new();

    public ReorganizationPlanDto? CreatePlan(
        Guid libraryId,
        CreateReorganizationPlanRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items is null)
            throw new ArgumentException("The reorganization candidate list is required.", nameof(request));
        ct.ThrowIfCancellationRequested();
        RemoveExpiredPlans(DateTimeOffset.UtcNow);

        var library = FindLibrary(libraryId);
        if (library is null) return null;

        var policies = CreatePolicies(library);
        var byId = policies.ToDictionary(policy => policy.SourceId, StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ReorganizationCandidate>(request.Items.Count);
        var existingPaths = new HashSet<string>(PathSafetyComparer);
        foreach (var item in request.Items)
        {
            ct.ThrowIfCancellationRequested();
            if (item.SourceId == Guid.Empty || item.DestinationSourceId == Guid.Empty)
                throw new ArgumentException("Source and destination IDs must be non-empty GUIDs.", nameof(request));
            ArgumentException.ThrowIfNullOrWhiteSpace(item.CurrentPath);

            var sourceId = item.SourceId.ToString("D");
            var destinationId = item.DestinationSourceId.ToString("D");
            var currentExists = fileSystem.FileExists(item.CurrentPath);
            var size = currentExists ? fileSystem.GetFileLength(item.CurrentPath) : 0;
            if (!string.IsNullOrWhiteSpace(item.ProposedPath)
                && (fileSystem.FileExists(item.ProposedPath) || fileSystem.DirectoryExists(item.ProposedPath)))
            {
                existingPaths.Add(item.ProposedPath);
            }

            candidates.Add(new ReorganizationCandidate
            {
                SourceId = sourceId,
                DestinationSourceId = destinationId,
                CurrentPath = item.CurrentPath,
                ProposedPath = item.ProposedPath,
                SizeBytes = size,
                UnresolvedReason = item.UnresolvedReason,
                Error = !currentExists
                    ? "The current file does not exist."
                    : !byId.ContainsKey(sourceId) || !byId.ContainsKey(destinationId)
                        ? "A stable source identity is not part of this library."
                        : null,
            });
        }

        var availableBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in policies)
        {
            try
            {
                availableBytes[policy.SourceId] = fileSystem.GetAvailableBytes(policy.RootPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Unknown capacity is treated as zero, so cross-source moves
                // cannot be confirmed until capacity can be proven.
                availableBytes[policy.SourceId] = 0;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var plan = planner.CreatePlan(new ReorganizationPlanningRequest
        {
            PlanId = Guid.NewGuid(),
            LibraryId = library.Id,
            Sources = policies,
            Candidates = candidates,
            ExistingPaths = existingPaths,
            AvailableBytesByDestinationSource = availableBytes,
            CreatedAt = now,
        });
        var expiresAt = now + PlanLifetime;
        _plans[plan.Id] = new PendingPlan(
            plan,
            policies.ToDictionary(policy => policy.SourceId, StringComparer.OrdinalIgnoreCase),
            expiresAt);
        return ToDto(plan, libraryId, expiresAt);
    }

    public ReorganizationExecutionDto? Execute(
        Guid libraryId,
        ExecuteReorganizationPlanRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (!_plans.TryGetValue(request.PlanId, out var pending)
            || !Guid.TryParse(pending.Plan.LibraryId, out var plannedLibraryId)
            || plannedLibraryId != libraryId)
        {
            return null;
        }
        if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _plans.TryRemove(request.PlanId, out _);
            throw new InvalidOperationException("The reorganization preview expired; create a new plan.");
        }
        if (!string.Equals(pending.Plan.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The confirmed fingerprint does not match the previewed plan.");

        if (!_plans.TryRemove(request.PlanId, out pending))
            return null;

        var confirmed = pending.Plan.Confirm(request.Fingerprint, DateTimeOffset.UtcNow);
        var result = executor.Execute(
            confirmed,
            pending.Policies,
            () => ResolveCurrentPolicies(libraryId),
            ct);
        return new ReorganizationExecutionDto(
            result.PlanId,
            libraryId,
            result.Fingerprint,
            result.Succeeded,
            result.Blocked,
            result.Failed,
            result.Items.Select(item => new ReorganizationExecutionItemDto(
                item.Sequence,
                item.CurrentPath,
                item.ProposedPath,
                item.Disposition.ToString().ToLowerInvariant(),
                item.Reason)).ToList());
    }

    private IReadOnlyList<FileSourceMutationPolicy> ResolveCurrentPolicies(Guid libraryId)
    {
        var library = FindLibrary(libraryId);
        return library is null ? [] : CreatePolicies(library);
    }

    private LibraryFolderConfig? FindLibrary(Guid libraryId) =>
        configuration.LoadLibraries().Libraries.FirstOrDefault(library =>
            Guid.TryParse(library.Id, out var id) && id == libraryId);

    private static IReadOnlyList<FileSourceMutationPolicy> CreatePolicies(LibraryFolderConfig library) =>
        library.Sources.Select(source => FileSourceMutationPolicyFactory.Create(library, source)).ToList();

    private void RemoveExpiredPlans(DateTimeOffset now)
    {
        foreach (var pair in _plans.Where(pair => pair.Value.ExpiresAt <= now))
            _plans.TryRemove(pair.Key, out _);
    }

    private static ReorganizationPlanDto ToDto(
        ReorganizationPlan plan,
        Guid libraryId,
        DateTimeOffset expiresAt) => new(
        plan.Id,
        libraryId,
        plan.CreatedAt,
        expiresAt,
        plan.Fingerprint,
        plan.CanConfirm,
        new ReorganizationPlanSummaryDto(
            plan.Summary.Total,
            plan.Summary.Unchanged,
            plan.Summary.Renamed,
            plan.Summary.Moved,
            plan.Summary.Conflicts,
            plan.Summary.Unresolved,
            plan.Summary.Blocked,
            plan.Summary.Errors),
        plan.Operations.Select(operation => new ReorganizationPlanItemDto(
            operation.Sequence,
            Guid.Parse(operation.SourceId),
            Guid.Parse(operation.DestinationSourceId),
            operation.CurrentPath,
            operation.ProposedPath,
            operation.Disposition.ToString().ToLowerInvariant(),
            operation.SizeBytes,
            operation.Reason)).ToList());

    private static StringComparer PathSafetyComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record PendingPlan(
        ReorganizationPlan Plan,
        IReadOnlyDictionary<string, FileSourceMutationPolicy> Policies,
        DateTimeOffset ExpiresAt);
}
