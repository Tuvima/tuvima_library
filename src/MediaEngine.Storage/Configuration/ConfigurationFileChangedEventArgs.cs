namespace MediaEngine.Storage.Configuration;

public sealed class ConfigurationFileChangedEventArgs : EventArgs
{
    public ConfigurationFileChangedEventArgs(string relativePath, string fullPath, bool applied, Exception? error)
    {
        RelativePath = relativePath;
        FullPath = fullPath;
        Applied = applied;
        Error = error;
    }

    public string RelativePath { get; }
    public string FullPath { get; }
    public bool Applied { get; }
    public Exception? Error { get; }
}
