using MediaEngine.Ingestion.Models;
using MediaEngine.Ingestion.Services;
using MediaEngine.Domain.Configuration;

namespace MediaEngine.Ingestion.Tests;

public sealed class SourceMutationPolicyGateTests
{
    private readonly SourceMutationPolicyGate _gate = new();

    [Theory]
    [InlineData(SourceMutationKind.Move)]
    [InlineData(SourceMutationKind.Rename)]
    [InlineData(SourceMutationKind.MetadataWriteback)]
    [InlineData(SourceMutationKind.Delete)]
    [InlineData(SourceMutationKind.UseAsDestination)]
    public void ExistingLibrary_DeniesEveryFilesystemMutation(SourceMutationKind mutation)
    {
        var root = NewRoot();
        var policy = Policy(root, FileSourceManagementMode.ExistingLibrary);

        var result = _gate.Evaluate(new SourceMutationRequest
        {
            Source = policy,
            Mutation = mutation,
            Path = Path.Combine(root, "media", "item.mkv"),
        });

        Assert.False(result.Allowed);
        Assert.Contains("read-only", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedSource_MustBeWritable()
    {
        var root = NewRoot();
        var policy = Policy(root, FileSourceManagementMode.ManagedByTuvima) with
        {
            IsWritable = false,
        };

        var result = _gate.Evaluate(new SourceMutationRequest
        {
            Source = policy,
            Mutation = SourceMutationKind.Move,
            Path = Path.Combine(root, "item.mkv"),
        });

        Assert.False(result.Allowed);
        Assert.Contains("not writable", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedSource_RequiresActionSpecificPermission()
    {
        var root = NewRoot();
        var policy = Policy(root, FileSourceManagementMode.ManagedByTuvima) with
        {
            AllowDelete = false,
        };

        var result = _gate.Evaluate(new SourceMutationRequest
        {
            Source = policy,
            Mutation = SourceMutationKind.Delete,
            Path = Path.Combine(root, "item.mkv"),
        });

        Assert.False(result.Allowed);
        Assert.Contains(nameof(SourceMutationKind.Delete), result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedSource_DeniesPathOutsideConfiguredRoot()
    {
        var root = NewRoot();
        var result = _gate.Evaluate(new SourceMutationRequest
        {
            Source = Policy(root, FileSourceManagementMode.ManagedByTuvima),
            Mutation = SourceMutationKind.Move,
            Path = Path.Combine(Path.GetDirectoryName(root)!, "different-source", "item.mkv"),
        });

        Assert.False(result.Allowed);
        Assert.Contains("outside", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManagedWritableSource_PermitsEnabledActionInsideRoot()
    {
        var root = NewRoot();
        var result = _gate.Evaluate(new SourceMutationRequest
        {
            Source = Policy(root, FileSourceManagementMode.ManagedByTuvima),
            Mutation = SourceMutationKind.MetadataWriteback,
            Path = Path.Combine(root, "item.flac"),
        });

        Assert.True(result.Allowed);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void ConfigurationFactory_DefaultsUnknownModesToExistingLibrarySafety()
    {
        var source = new LibrarySourceConfig
        {
            Id = "source",
            Path = NewRoot(),
            ManagementMode = "unexpected-mode",
            AccessMode = LibrarySourceAccessModes.Writable,
            ParticipatesInOrganization = true,
            WritebackOverride = true,
        };
        var library = new LibraryFolderConfig
        {
            Id = "library",
            Sources = [source],
        };

        var policy = FileSourceMutationPolicyFactory.Create(
            library,
            source,
            globalMetadataWritebackEnabled: true,
            allowDelete: true);

        Assert.Equal(FileSourceManagementMode.ExistingLibrary, policy.ManagementMode);
        Assert.False(policy.AllowMove);
        Assert.False(policy.AllowRename);
        Assert.False(policy.AllowMetadataWriteback);
        Assert.False(policy.AllowDelete);
        Assert.False(policy.AllowAsDestination);
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "tuvima-source-policy", Guid.NewGuid().ToString("N"));

    internal static FileSourceMutationPolicy Policy(string root, FileSourceManagementMode mode, string sourceId = "source") => new()
    {
        LibraryId = "library",
        SourceId = sourceId,
        RootPath = root,
        ManagementMode = mode,
        IsWritable = true,
        AllowMove = true,
        AllowRename = true,
        AllowMetadataWriteback = true,
        AllowDelete = true,
        AllowAsDestination = true,
    };
}
