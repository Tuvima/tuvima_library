using MediaEngine.Ingestion.Models;

namespace MediaEngine.Ingestion.Contracts;

public interface IReorganizationExecutor
{
    ReorganizationExecutionResult Execute(
        ReorganizationPlan confirmedPlan,
        IReadOnlyDictionary<string, FileSourceMutationPolicy> plannedPolicies,
        Func<IReadOnlyList<FileSourceMutationPolicy>> currentPoliciesResolver,
        CancellationToken ct = default);
}

public interface IReorganizationFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    long GetFileLength(string path);
    long GetAvailableBytes(string destinationPath);
    void CreateDirectory(string path);
    void MoveFile(string currentPath, string proposedPath);
}
