using MediaEngine.Api.Models;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface ICollectionBrowseReadService
{
    Task<List<CollectionDto>> GetAllAsync(CancellationToken ct);
}

public sealed class CollectionBrowseReadService(
    ICollectionRepository collectionRepo,
    IDatabaseConnection db) : ICollectionBrowseReadService
{
    public async Task<List<CollectionDto>> GetAllAsync(CancellationToken ct)
    {
        var collections = await collectionRepo.GetAllAsync(ct);

        var libraryWorkIds = new HashSet<Guid>();
        using (var conn = db.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT DISTINCT e.work_id
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE ma.file_path_root NOT LIKE '%/.data/staging/%'
                  AND ma.file_path_root NOT LIKE '%\.data\staging\%'
                  AND ma.file_path_root NOT LIKE '%/.data\staging/%'
                """;
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (Guid.TryParse(reader.GetString(0), out var workId))
                {
                    libraryWorkIds.Add(workId);
                }
            }
        }

        var filtered = new List<CollectionDto>();
        foreach (var collection in collections)
        {
            var libraryWorks = collection.Works.Where(work => libraryWorkIds.Contains(work.Id)).ToList();
            if (libraryWorks.Count == 0)
            {
                continue;
            }

            var filteredCollection = new Collection
            {
                Id = collection.Id,
                UniverseId = collection.UniverseId,
                DisplayName = collection.DisplayName,
                CreatedAt = collection.CreatedAt,
                UniverseStatus = collection.UniverseStatus,
                ParentCollectionId = collection.ParentCollectionId,
                WikidataQid = collection.WikidataQid,
            };

            foreach (var work in libraryWorks)
            {
                filteredCollection.AddWork(work);
            }

            foreach (var relationship in collection.Relationships)
            {
                filteredCollection.AddRelationship(relationship);
            }

            filtered.Add(CollectionDto.FromDomain(filteredCollection));
        }

        return filtered;
    }
}
