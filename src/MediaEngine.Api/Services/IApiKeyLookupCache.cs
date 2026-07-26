using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Api.Services;

/// <summary>
/// Caches API-key lookups so authentication does not open a SQLite connection
/// for every request.
/// </summary>
public interface IApiKeyLookupCache
{
    Task<ApiKey?> FindByHashedKeyAsync(
        string hashedKey,
        CancellationToken ct = default);

    void InvalidateAll();
}
