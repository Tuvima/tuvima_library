namespace MediaEngine.Providers.Contracts;

/// <summary>
/// Receives credential changes without requiring the Engine to rebuild provider
/// singletons. Values are write-only runtime material and must never be logged.
/// </summary>
public interface IProviderCredentialConsumer
{
    string Name { get; }

    void ApplyCredentials(IReadOnlyDictionary<string, string?> credentials);
}
