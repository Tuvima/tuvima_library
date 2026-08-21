using Dapper;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Playback;

namespace MediaEngine.Api.Services.Playback;

/// <summary>
/// Stores explicit, user-authored display title overrides for embedded audiobook tracks.
/// It intentionally performs no title inference, chapter matching, or automated renaming.
/// </summary>
public sealed class AudiobookChapterNamingService(
    IDatabaseConnection db,
    AudiobookChapterTitleOverrideRepository overrides)
{
    public Task<IReadOnlyList<AudiobookChapterTitleOverrideDto>> GetOverridesAsync(
        Guid workId,
        Guid? assetId = null,
        CancellationToken ct = default) =>
        overrides.GetByWorkAsync(workId, assetId, ct);

    public async Task<AudiobookChapterTitleOverrideDto> UpsertOverrideAsync(
        Guid workId,
        UpsertAudiobookChapterTitleOverrideRequestDto request,
        CancellationToken ct = default)
    {
        if (!await OwnsAudiobookAssetAsync(workId, request.AssetId, ct))
        {
            throw new KeyNotFoundException($"Audiobook asset '{request.AssetId}' was not found for work '{workId}'.");
        }

        return await overrides.UpsertAsync(workId, request, ct);
    }

    public Task<bool> DeleteOverrideAsync(Guid workId, Guid assetId, int chapterIndex, CancellationToken ct = default) =>
        overrides.DeleteAsync(workId, assetId, chapterIndex, ct);

    private async Task<bool> OwnsAudiobookAssetAsync(Guid workId, Guid assetId, CancellationToken ct)
    {
        using var connection = db.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE w.id = @workId
                  AND ma.id = @assetId
                  AND LOWER(w.media_type) IN ('audiobook', 'audiobooks', 'audio')
            );
            """,
            new { workId, assetId },
            cancellationToken: ct));
    }
}
