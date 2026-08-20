using MediaEngine.Ingestion.Contracts;

namespace MediaEngine.Ingestion;

public sealed class SystemReorganizationFileSystem : IReorganizationFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public long GetFileLength(string path) => new FileInfo(path).Length;

    public long GetAvailableBytes(string destinationPath)
    {
        var root = Path.GetPathRoot(destinationPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new IOException("The destination volume could not be resolved.");

        return new DriveInfo(root).AvailableFreeSpace;
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void MoveFile(string currentPath, string proposedPath) =>
        File.Move(currentPath, proposedPath, overwrite: false);
}
