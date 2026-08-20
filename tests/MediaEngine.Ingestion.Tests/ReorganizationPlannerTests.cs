using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Tests;

public sealed class ReorganizationPlannerTests
{
    private readonly ReorganizationPlanner _planner = new(new SourceMutationPolicyGate());

    [Fact]
    public void CreatePlan_ClassifiesUnchangedRenameAndMove()
    {
        var root = NewRoot("managed");
        var source = SourceMutationPolicyGateTests.Policy(root, FileSourceManagementMode.ManagedByTuvima);
        var currentUnchanged = Path.Combine(root, "Same.mkv");
        var currentRename = Path.Combine(root, "Old.mkv");
        var currentMove = Path.Combine(root, "Incoming", "Move.mkv");

        var plan = _planner.CreatePlan(Request(
            [source],
            [
                Candidate(source, source, currentUnchanged, currentUnchanged),
                Candidate(source, source, currentRename, Path.Combine(root, "New.mkv")),
                Candidate(source, source, currentMove, Path.Combine(root, "Movies", "Move.mkv")),
            ]));

        Assert.Equal(3, plan.Summary.Total);
        Assert.Equal(1, plan.Summary.Unchanged);
        Assert.Equal(1, plan.Summary.Renamed);
        Assert.Equal(1, plan.Summary.Moved);
        Assert.True(plan.CanConfirm);
    }

    [Fact]
    public void CreatePlan_BlocksExistingLibraryWithoutTouchingFile()
    {
        var root = NewRoot("existing");
        Directory.CreateDirectory(root);
        var current = Path.Combine(root, "Original Name.txt");
        File.WriteAllText(current, "untouched");

        try
        {
            var source = SourceMutationPolicyGateTests.Policy(root, FileSourceManagementMode.ExistingLibrary);
            var plan = _planner.CreatePlan(Request(
                [source],
                [Candidate(source, source, current, Path.Combine(root, "Renamed.txt"))]));

            Assert.Equal(1, plan.Summary.Blocked);
            Assert.False(plan.CanConfirm);
            Assert.True(File.Exists(current));
            Assert.Equal("untouched", File.ReadAllText(current));
            Assert.False(File.Exists(Path.Combine(root, "Renamed.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ChangingModeWithoutExplicitCandidate_IsANoOpAndDoesNotMutate()
    {
        var root = NewRoot("mode-change");
        Directory.CreateDirectory(root);
        var current = Path.Combine(root, "Keep Me.txt");
        File.WriteAllText(current, "unchanged");

        try
        {
            var existing = SourceMutationPolicyGateTests.Policy(root, FileSourceManagementMode.ExistingLibrary);
            var managed = existing with { ManagementMode = FileSourceManagementMode.ManagedByTuvima };

            var plan = _planner.CreatePlan(Request([managed], []));

            Assert.True(plan.IsNoOp);
            Assert.False(plan.CanConfirm);
            Assert.True(File.Exists(current));
            Assert.Equal("unchanged", File.ReadAllText(current));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreatePlan_BlocksExistingLibraryAsDestination()
    {
        var managedRoot = NewRoot("managed");
        var existingRoot = NewRoot("existing");
        var source = SourceMutationPolicyGateTests.Policy(managedRoot, FileSourceManagementMode.ManagedByTuvima, "managed");
        var destination = SourceMutationPolicyGateTests.Policy(existingRoot, FileSourceManagementMode.ExistingLibrary, "external");

        var plan = _planner.CreatePlan(Request(
            [source, destination],
            [Candidate(source, destination, Path.Combine(managedRoot, "item.mkv"), Path.Combine(existingRoot, "item.mkv"))]));

        Assert.Equal(1, plan.Summary.Blocked);
        Assert.Contains("read-only", plan.Operations[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreatePlan_DetectsExistingAndIntraPlanCollisions()
    {
        var root = NewRoot("managed");
        var source = SourceMutationPolicyGateTests.Policy(root, FileSourceManagementMode.ManagedByTuvima);
        var occupied = Path.Combine(root, "Organized", "Same.mkv");
        var duplicate = Path.Combine(root, "Organized", "Duplicate.mkv");

        var request = Request(
            [source],
            [
                Candidate(source, source, Path.Combine(root, "a.mkv"), occupied),
                Candidate(source, source, Path.Combine(root, "b.mkv"), duplicate),
                Candidate(source, source, Path.Combine(root, "c.mkv"), duplicate),
            ]) with
        {
            ExistingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { occupied },
        };

        var plan = _planner.CreatePlan(request);

        Assert.Equal(2, plan.Summary.Conflicts);
        Assert.Equal(1, plan.Summary.Moved);
        Assert.False(plan.CanConfirm);
    }

    [Fact]
    public void CreatePlan_BlocksOverlappingSourceDefinitions()
    {
        var outerRoot = NewRoot("library");
        var innerRoot = Path.Combine(outerRoot, "nested");
        var outer = SourceMutationPolicyGateTests.Policy(outerRoot, FileSourceManagementMode.ManagedByTuvima, "outer");
        var inner = SourceMutationPolicyGateTests.Policy(innerRoot, FileSourceManagementMode.ManagedByTuvima, "inner");

        var plan = _planner.CreatePlan(Request(
            [outer, inner],
            [Candidate(outer, inner, Path.Combine(outerRoot, "item.mkv"), Path.Combine(innerRoot, "item.mkv"))]));

        Assert.Equal(1, plan.Summary.Blocked);
        Assert.Contains("overlap", plan.Operations[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreatePlan_AccountsForDestinationDiskSpace()
    {
        var sourceRoot = NewRoot("source");
        var destinationRoot = NewRoot("destination");
        var source = SourceMutationPolicyGateTests.Policy(sourceRoot, FileSourceManagementMode.ManagedByTuvima, "source");
        var destination = SourceMutationPolicyGateTests.Policy(destinationRoot, FileSourceManagementMode.ManagedByTuvima, "destination");

        var request = Request(
            [source, destination],
            [
                Candidate(source, destination, Path.Combine(sourceRoot, "one.mkv"), Path.Combine(destinationRoot, "one.mkv"), 60),
                Candidate(source, destination, Path.Combine(sourceRoot, "two.mkv"), Path.Combine(destinationRoot, "two.mkv"), 60),
            ]) with
        {
            AvailableBytesByDestinationSource = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                [destination.SourceId] = 100,
            },
        };

        var plan = _planner.CreatePlan(request);

        Assert.Equal(1, plan.Summary.Moved);
        Assert.Equal(1, plan.Summary.Blocked);
        Assert.Contains("disk space", plan.Operations[1].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreatePlan_TracksUnresolvedAndErrors()
    {
        var root = NewRoot("managed");
        var source = SourceMutationPolicyGateTests.Policy(root, FileSourceManagementMode.ManagedByTuvima);

        var plan = _planner.CreatePlan(Request(
            [source],
            [
                Candidate(source, source, Path.Combine(root, "unknown.bin"), null) with { UnresolvedReason = "No template result." },
                Candidate(source, source, Path.Combine(root, "bad.bin"), Path.Combine(root, "bad-2.bin")) with { Error = "Metadata extraction failed." },
            ]));

        Assert.Equal(1, plan.Summary.Unresolved);
        Assert.Equal(1, plan.Summary.Errors);
        Assert.False(plan.CanConfirm);
    }

    [Fact]
    public void Confirm_RequiresExactPreviewFingerprintAndReadyPlan()
    {
        var root = NewRoot("managed");
        var source = SourceMutationPolicyGateTests.Policy(root, FileSourceManagementMode.ManagedByTuvima);
        var plan = _planner.CreatePlan(Request(
            [source],
            [Candidate(source, source, Path.Combine(root, "old.mkv"), Path.Combine(root, "new.mkv"))]));
        var confirmedAt = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        Assert.Throws<InvalidOperationException>(() => plan.GetConfirmedOperations());
        Assert.Throws<InvalidOperationException>(() => plan.Confirm("stale-preview", confirmedAt));

        var confirmed = plan.Confirm(plan.Fingerprint, confirmedAt);

        Assert.Equal(ReorganizationPlanStatus.Confirmed, confirmed.Status);
        Assert.Equal(confirmedAt, confirmed.ConfirmedAt);
        Assert.True(confirmed.CanExecute);
        Assert.Single(confirmed.GetConfirmedOperations());
        Assert.Throws<InvalidOperationException>(() => confirmed.Confirm(confirmed.Fingerprint, confirmedAt));
    }

    [Fact]
    public void NoOpPlan_CannotBeConfirmed()
    {
        var root = NewRoot("managed");
        var source = SourceMutationPolicyGateTests.Policy(root, FileSourceManagementMode.ManagedByTuvima);
        var path = Path.Combine(root, "same.mkv");
        var plan = _planner.CreatePlan(Request([source], [Candidate(source, source, path, path)]));

        Assert.True(plan.IsNoOp);
        Assert.False(plan.CanConfirm);
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(plan.Fingerprint, DateTimeOffset.UtcNow));
    }

    private static ReorganizationPlanningRequest Request(
        IReadOnlyList<FileSourceMutationPolicy> sources,
        IReadOnlyList<ReorganizationCandidate> candidates) => new()
    {
        PlanId = Guid.Parse("b1741b2b-f691-4013-a8c4-f3aee5559c6b"),
        LibraryId = "library",
        Sources = sources,
        Candidates = candidates,
        CreatedAt = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero),
    };

    private static ReorganizationCandidate Candidate(
        FileSourceMutationPolicy source,
        FileSourceMutationPolicy destination,
        string current,
        string? proposed,
        long sizeBytes = 1) => new()
    {
        SourceId = source.SourceId,
        DestinationSourceId = destination.SourceId,
        CurrentPath = current,
        ProposedPath = proposed,
        SizeBytes = sizeBytes,
    };

    private static string NewRoot(string name)
        => Path.Combine(Path.GetTempPath(), "tuvima-reorganization", Guid.NewGuid().ToString("N"), name);
}
