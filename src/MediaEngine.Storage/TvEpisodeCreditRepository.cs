using System.Text.Json;
using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
namespace MediaEngine.Storage;

public sealed class TvEpisodeCreditRepository(IDatabaseConnection db) : ITvEpisodeCreditRepository
{
    public Task ReplaceAsync(TvEpisodeCredits credits, CancellationToken ct = default) =>
        db.ExecuteWriteAsync((conn, tx, token) =>
        {
            conn.Execute("""
                INSERT INTO tv_episode_credits(work_id, show_work_id, provider_episode_id, season, episode, credits_json)
                VALUES(@WorkId, @ShowWorkId, @ProviderEpisodeId, @Season, @Episode, @json)
                ON CONFLICT(work_id) DO UPDATE SET show_work_id=excluded.show_work_id,
                  provider_episode_id=excluded.provider_episode_id, season=excluded.season,
                  episode=excluded.episode, credits_json=excluded.credits_json;
                """, new
            {
                credits.WorkId,
                credits.ShowWorkId,
                credits.ProviderEpisodeId,
                credits.Season,
                credits.Episode,
                json = JsonSerializer.Serialize(credits)
            }, tx);
        }, ct);

    public Task<IReadOnlyList<TvEpisodeCredits>> ReadAsync(Guid workOrShowId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        var json = conn.Query<string>(new CommandDefinition("""
            SELECT tc.credits_json FROM tv_episode_credits tc
            WHERE (tc.work_id=@workOrShowId OR tc.show_work_id=@workOrShowId)
              AND EXISTS (SELECT 1 FROM editions e JOIN media_assets ma ON ma.edition_id=e.id
                          WHERE e.work_id=tc.work_id AND ma.status='Normal' AND ma.is_orphaned=0)
            ORDER BY tc.season, tc.episode;
            """, new { workOrShowId }, cancellationToken: ct));
        return Task.FromResult<IReadOnlyList<TvEpisodeCredits>>(json.Select(value => JsonSerializer.Deserialize<TvEpisodeCredits>(value)!).ToList());
    }
}
