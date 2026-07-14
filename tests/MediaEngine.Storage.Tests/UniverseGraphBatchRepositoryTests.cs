using Dapper;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage;

namespace MediaEngine.Storage.Tests;

public sealed class UniverseGraphBatchRepositoryTests : IDisposable
{
    private const int LargeGraphSize = 1_105;
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public UniverseGraphBatchRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_graph_batch_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
    }

    [Fact]
    public async Task WorkLinksAndPeople_LoadAcrossMultipleParameterBatches()
    {
        var entityIds = Enumerable.Range(0, LargeGraphSize).Select(_ => Guid.NewGuid()).ToArray();
        var qids = Enumerable.Range(0, LargeGraphSize).Select(index => $"Q{index + 1}").ToArray();
        var personIds = Enumerable.Range(0, LargeGraphSize).Select(_ => Guid.NewGuid()).ToArray();
        var createdAt = DateTimeOffset.UtcNow.ToString("O");

        using (var conn = _db.CreateConnection())
        using (var tx = conn.BeginTransaction())
        {
            for (var index = 0; index < LargeGraphSize; index++)
            {
                conn.Execute("""
                    INSERT INTO fictional_entities
                        (id, wikidata_qid, label, entity_sub_type, created_at)
                    VALUES (@Id, @Qid, @Label, 'Character', @CreatedAt);

                    INSERT INTO fictional_entity_work_links
                        (entity_id, work_qid, work_label, link_type)
                    VALUES (@Id, @WorkQid, @WorkLabel, 'appears_in');

                    INSERT INTO persons
                        (id, name, wikidata_qid, headshot_url, created_at)
                    VALUES (@PersonId, @PersonName, @PersonQid, @HeadshotUrl, @CreatedAt);
                    """, new
                    {
                        Id = GuidSql.ToBlob(entityIds[index]),
                        Qid = qids[index],
                        Label = $"Character {index}",
                        CreatedAt = createdAt,
                        WorkQid = $"W{index}",
                        WorkLabel = $"Work {index}",
                        PersonId = GuidSql.ToBlob(personIds[index]),
                        PersonName = $"Actor {index}",
                        PersonQid = $"P{index}",
                        HeadshotUrl = $"https://example.test/{index}.jpg",
                    }, tx);
            }

            conn.Execute("""
                INSERT INTO person_roles (person_id, role)
                VALUES (@PersonId, 'Actor');
                """, new { PersonId = GuidSql.ToBlob(personIds[0]) }, tx);
            tx.Commit();
        }

        var links = await new FictionalEntityRepository(_db).GetWorkLinksAsync(entityIds);
        Assert.Equal(LargeGraphSize, links.Count);
        Assert.Contains(links, link => link.FictionalEntityId == entityIds[^1] && link.WorkQid == $"W{LargeGraphSize - 1}");

        var people = await new PersonRepository(_db).FindByQidsAsync(
            Enumerable.Range(0, LargeGraphSize).Select(index => $"p{index}"));
        Assert.Equal(LargeGraphSize, people.Count);
        Assert.Contains(people, person => person.WikidataQid == "P0" && person.Roles.SequenceEqual(["Actor"]));
    }

    [Fact]
    public async Task RelationshipBatches_PreserveCrossBatchUniverseEdgesAndObjectReads()
    {
        var universeQids = Enumerable.Range(0, LargeGraphSize)
            .Select(index => $"Q{index + 1}")
            .ToArray();
        var repository = new EntityRelationshipRepository(_db);

        var internalEdges = new[]
        {
            Edge("Q1", "Q700", "member_of"),
            Edge("Q700", $"Q{LargeGraphSize}", "sibling"),
        };
        foreach (var edge in internalEdges)
            await repository.CreateAsync(edge);

        await repository.CreateAsync(Edge("Q1", "Q999999", "located_in"));
        await repository.CreateAsync(Edge("P1", "Q700", "performer"));

        var universeEdges = await repository.GetByUniverseAsync(
            universeQids.Select(qid => qid.ToLowerInvariant()).ToArray());
        Assert.Equal(2, universeEdges.Count);
        Assert.Contains(universeEdges, edge => edge.SubjectQid == "Q1" && edge.ObjectQid == "Q700");
        Assert.Contains(universeEdges, edge => edge.SubjectQid == "Q700" && edge.ObjectQid == $"Q{LargeGraphSize}");

        var incomingEdges = await repository.GetByObjectsAsync(
            universeQids.Select(qid => qid.ToLowerInvariant()));
        Assert.Contains(incomingEdges, edge => edge.SubjectQid == "P1" && edge.ObjectQid == "Q700");
    }

    public void Dispose()
    {
        _db.Dispose();
        TryDelete(_dbPath);
        TryDelete($"{_dbPath}-wal");
        TryDelete($"{_dbPath}-shm");
    }

    private static EntityRelationship Edge(string subject, string target, string type) => new()
    {
        Id = Guid.NewGuid(),
        SubjectQid = subject,
        ObjectQid = target,
        RelationshipTypeValue = type,
        Confidence = 0.9,
        DiscoveredAt = DateTimeOffset.UtcNow,
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // SQLite pool cleanup can briefly retain a test file on Windows.
        }
    }
}
