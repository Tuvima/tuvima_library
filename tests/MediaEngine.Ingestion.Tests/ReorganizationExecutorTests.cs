using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Tests;

public sealed class ReorganizationExecutorTests
{
    private readonly ReorganizationPlanner _planner = new(new SourceMutationPolicyGate());

    [Fact]
    public void Execute_MovesOnlyTheExactConfirmedOperation()
    {
        var root = NewRoot("managed");
        Directory.CreateDirectory(root);
        var current = Path.Combine(root, "incoming", "item.txt");
        var proposed = Path.Combine(root, "organized", "item.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.WriteAllText(current, "original");

        try
        {
            var policy = Policy(root);
            var plan = ConfirmedPlan([policy], [Candidate(policy, policy, current, proposed, new FileInfo(current).Length)]);
            var resolverCalls = 0;
            var result = Executor().Execute(plan, Snapshot(policy), () =>
            {
                resolverCalls++;
                return [policy];
            });

            Assert.Equal(1, resolverCalls);
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(ReorganizationExecutionDisposition.Moved, Assert.Single(result.Items).Disposition);
            Assert.False(File.Exists(current));
            Assert.Equal("original", File.ReadAllText(proposed));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Execute_BlocksWhenExactSourcePolicyChangedAfterPreview()
    {
        var root = NewRoot("managed");
        Directory.CreateDirectory(root);
        var current = Path.Combine(root, "item.txt");
        var proposed = Path.Combine(root, "renamed.txt");
        File.WriteAllText(current, "keep");

        try
        {
            var planned = Policy(root);
            var currentPolicy = planned with
            {
                ManagementMode = FileSourceManagementMode.ExistingLibrary,
                AllowMove = false,
                AllowRename = false,
                AllowAsDestination = false,
            };
            var plan = ConfirmedPlan([planned], [Candidate(planned, planned, current, proposed, new FileInfo(current).Length)]);

            var item = Assert.Single(Executor().Execute(plan, Snapshot(planned), () => [currentPolicy]).Items);

            Assert.Equal(ReorganizationExecutionDisposition.Blocked, item.Disposition);
            Assert.Contains("changed after preview", item.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(current));
            Assert.False(File.Exists(proposed));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Execute_NeverOverwritesDestinationCreatedAfterPreview()
    {
        var root = NewRoot("managed");
        Directory.CreateDirectory(root);
        var current = Path.Combine(root, "item.txt");
        var proposed = Path.Combine(root, "renamed.txt");
        File.WriteAllText(current, "source");
        var policy = Policy(root);
        var plan = ConfirmedPlan([policy], [Candidate(policy, policy, current, proposed, new FileInfo(current).Length)]);
        File.WriteAllText(proposed, "destination");

        try
        {
            var item = Assert.Single(Executor().Execute(plan, Snapshot(policy), () => [policy]).Items);

            Assert.Equal(ReorganizationExecutionDisposition.Blocked, item.Disposition);
            Assert.Contains("destination", item.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("source", File.ReadAllText(current));
            Assert.Equal("destination", File.ReadAllText(proposed));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Execute_RechecksDestinationCapacityImmediatelyBeforeMove()
    {
        var source = Policy(NewRoot("source"), "source");
        var destination = Policy(NewRoot("destination"), "destination");
        var current = Path.Combine(source.RootPath, "item.bin");
        var proposed = Path.Combine(destination.RootPath, "item.bin");
        var fileSystem = new FakeFileSystem((current, 100)) { AvailableBytes = 99 };
        var plan = ConfirmedPlan(
            [source, destination],
            [Candidate(source, destination, current, proposed, 100)],
            new Dictionary<string, long> { [destination.SourceId] = 1_000 });

        var item = Assert.Single(Executor(fileSystem).Execute(
            plan,
            Snapshot(source, destination),
            () => [source, destination]).Items);

        Assert.Equal(ReorganizationExecutionDisposition.Blocked, item.Disposition);
        Assert.Contains("disk space", item.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(fileSystem.FileExists(current));
        Assert.False(fileSystem.FileExists(proposed));
    }

    [Fact]
    public void Execute_ContinuesAfterAnIndependentItemFails()
    {
        var policy = Policy(NewRoot("managed"));
        var first = Path.Combine(policy.RootPath, "first.bin");
        var second = Path.Combine(policy.RootPath, "second.bin");
        var firstDestination = Path.Combine(policy.RootPath, "organized", "first.bin");
        var secondDestination = Path.Combine(policy.RootPath, "organized", "second.bin");
        var fileSystem = new FakeFileSystem((first, 10), (second, 20)) { ThrowMoveFor = first };
        var plan = ConfirmedPlan(
            [policy],
            [Candidate(policy, policy, first, firstDestination, 10), Candidate(policy, policy, second, secondDestination, 20)]);
        var resolverCalls = 0;

        var result = Executor(fileSystem).Execute(plan, Snapshot(policy), () =>
        {
            resolverCalls++;
            return [policy];
        });

        Assert.Equal(2, resolverCalls);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Succeeded);
        Assert.True(fileSystem.FileExists(first));
        Assert.True(fileSystem.FileExists(secondDestination));
    }

    private ReorganizationPlan ConfirmedPlan(
        IReadOnlyList<FileSourceMutationPolicy> policies,
        IReadOnlyList<ReorganizationCandidate> candidates,
        IReadOnlyDictionary<string, long>? availableBytes = null)
    {
        var plan = _planner.CreatePlan(new ReorganizationPlanningRequest
        {
            PlanId = Guid.NewGuid(),
            LibraryId = "library",
            Sources = policies,
            Candidates = candidates,
            AvailableBytesByDestinationSource = availableBytes
                ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
        });
        return plan.Confirm(plan.Fingerprint, DateTimeOffset.UtcNow);
    }

    private static ReorganizationCandidate Candidate(
        FileSourceMutationPolicy source,
        FileSourceMutationPolicy destination,
        string current,
        string proposed,
        long size) => new()
    {
        SourceId = source.SourceId,
        DestinationSourceId = destination.SourceId,
        CurrentPath = current,
        ProposedPath = proposed,
        SizeBytes = size,
    };

    private static ReorganizationExecutor Executor(IReorganizationFileSystem? fileSystem = null)
        => new(new SourceMutationPolicyGate(), fileSystem ?? new SystemReorganizationFileSystem());

    private static FileSourceMutationPolicy Policy(string root, string sourceId = "source")
        => SourceMutationPolicyGateTests.Policy(root, FileSourceManagementMode.ManagedByTuvima, sourceId);

    private static IReadOnlyDictionary<string, FileSourceMutationPolicy> Snapshot(params FileSourceMutationPolicy[] policies)
        => policies.ToDictionary(policy => policy.SourceId, StringComparer.OrdinalIgnoreCase);

    private static string NewRoot(string name)
        => Path.Combine(Path.GetTempPath(), "tuvima-reorganization-executor", Guid.NewGuid().ToString("N"), name);

    private sealed class FakeFileSystem(params (string Path, long Length)[] files) : IReorganizationFileSystem
    {
        private readonly Dictionary<string, long> _files = files.ToDictionary(file => file.Path, file => file.Length, StringComparer.OrdinalIgnoreCase);
        public long AvailableBytes { get; set; } = long.MaxValue;
        public string? ThrowMoveFor { get; set; }

        public bool FileExists(string path) => _files.ContainsKey(path);
        public bool DirectoryExists(string path) => false;
        public long GetFileLength(string path) => _files[path];
        public long GetAvailableBytes(string destinationPath) => AvailableBytes;
        public void CreateDirectory(string path) { }

        public void MoveFile(string currentPath, string proposedPath)
        {
            if (string.Equals(currentPath, ThrowMoveFor, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Simulated move failure.");
            if (_files.ContainsKey(proposedPath)) throw new IOException("Destination exists.");
            var length = _files[currentPath];
            _files.Remove(currentPath);
            _files.Add(proposedPath, length);
        }
    }
}
