using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface IMetadataClaimHistoryReadService
{
    Task<List<ClaimDto>> GetClaimHistoryAsync(Guid entityId, CancellationToken ct);
}

public sealed class MetadataClaimHistoryReadService(
    IMetadataClaimRepository claimRepo,
    IDatabaseConnection db) : IMetadataClaimHistoryReadService
{
    public async Task<List<ClaimDto>> GetClaimHistoryAsync(Guid entityId, CancellationToken ct)
    {
        var claims = (await claimRepo.GetByEntityAsync(entityId, ct)).ToList();

        if (claims.Count == 0)
        {
            using var conn = db.CreateConnection();
            var assetIds = (await conn.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT ma.id
                FROM media_assets ma
                INNER JOIN editions e ON ma.edition_id = e.id
                WHERE e.work_id = @WorkId;
                """,
                new { WorkId = entityId },
                cancellationToken: ct))).ToList();

            if (assetIds.Count > 0)
            {
                var allClaims = new List<MetadataClaim>();
                foreach (var assetId in assetIds)
                    allClaims.AddRange(await claimRepo.GetByEntityAsync(assetId, ct));

                claims = allClaims
                    .GroupBy(c => (c.ClaimKey, c.ClaimValue, c.ProviderId, c.DecisionSourceProviderId))
                    .Select(g => g.OrderByDescending(c => c.Confidence).First())
                    .OrderBy(c => c.ClaimedAt)
                    .ToList();
            }
        }

        return claims.Select(ClaimDto.FromDomain).ToList();
    }
}
