using MediaEngine.Api.Endpoints;
using MediaEngine.Domain;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class PersonCreditQueriesTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public PersonCreditQueriesTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_person_credits_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task GetLibraryCreditsAsync_CollapsesTvEpisodesToSeriesAndPrefersActorEvidence()
    {
        var personId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");

        using (var conn = _db.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO collections (id, display_name, collection_type, wikidata_qid, created_at)
                    VALUES ($collectionId, 'Sample Crime Show', 'Series', 'QSHOW', $now);

                INSERT INTO persons (id, name, created_at)
                    VALUES ($personId, 'Bryan Test Person', $now);
                INSERT INTO person_roles (person_id, role)
                    VALUES ($personId, 'Director');

                INSERT INTO fictional_entities (
                    id, wikidata_qid, label, entity_sub_type, fictional_universe_qid,
                    fictional_universe_label, created_at
                )
                    VALUES ($characterId, 'QCHAR', 'Lead Chemistry Teacher', 'Character', 'QSHOW', 'Sample Crime Show', $now);
                INSERT INTO character_performer_links (person_id, fictional_entity_id, work_qid)
                    VALUES ($personId, $characterId, 'QSHOW');
                """;
            cmd.Parameters.AddWithValue("$collectionId", collectionId.ToString("D"));
            cmd.Parameters.AddWithValue("$personId", personId.ToString("D"));
            cmd.Parameters.AddWithValue("$characterId", characterId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();

            for (var episode = 1; episode <= 3; episode++)
            {
                var workId = Guid.NewGuid();
                var editionId = Guid.NewGuid();
                var assetId = Guid.NewGuid();

                using var episodeCmd = conn.CreateCommand();
                episodeCmd.CommandText = """
                    INSERT INTO works (id, collection_id, media_type, wikidata_qid, work_kind, ordinal)
                        VALUES ($workId, $collectionId, 'TV', $workQid, 'child', $episode);
                    INSERT INTO editions (id, work_id)
                        VALUES ($editionId, $workId);
                    INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                        VALUES ($assetId, $editionId, $hash, $path);
                    INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                        VALUES ($workId, 'title', $title, $now);
                    INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                        VALUES ($workId, 'year', '2008', $now);
                    INSERT INTO person_media_links (media_asset_id, person_id, role)
                        VALUES ($assetId, $personId, 'Director');
                    """;
                episodeCmd.Parameters.AddWithValue("$workId", workId.ToString("D"));
                episodeCmd.Parameters.AddWithValue("$collectionId", collectionId.ToString("D"));
                episodeCmd.Parameters.AddWithValue("$workQid", $"QEP{episode}");
                episodeCmd.Parameters.AddWithValue("$episode", episode);
                episodeCmd.Parameters.AddWithValue("$editionId", editionId.ToString("D"));
                episodeCmd.Parameters.AddWithValue("$assetId", assetId.ToString("D"));
                episodeCmd.Parameters.AddWithValue("$hash", $"sample-crime-show-{episode}");
                episodeCmd.Parameters.AddWithValue("$path", $"C:/library/Sample Crime Show/S01E0{episode}.mkv");
                episodeCmd.Parameters.AddWithValue("$title", $"Episode {episode}");
                episodeCmd.Parameters.AddWithValue("$personId", personId.ToString("D"));
                episodeCmd.Parameters.AddWithValue("$now", now);
                episodeCmd.ExecuteNonQuery();
            }
        }

        var credits = await PersonCreditQueries.GetLibraryCreditsAsync(personId, _db, CancellationToken.None);

        var credit = Assert.Single(credits);
        Assert.Equal(collectionId, credit.CollectionId);
        Assert.Equal("Sample Crime Show", credit.Title);
        Assert.Equal("Actor", credit.Role);
        Assert.Single(credit.Characters);
        Assert.Equal("Lead Chemistry Teacher", credit.Characters[0].CharacterName);
    }

    [Fact]
    public async Task BuildForWorkAsync_UsesTmdbCharacterClaimsWhenExplicitCharacterLinksAreMissing()
    {
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var providerId = WellKnownProviders.Tmdb;
        var now = DateTimeOffset.UtcNow.ToString("O");

        using (var conn = _db.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO metadata_providers (id, name, version, is_enabled)
                    VALUES ($providerId, 'tmdb', '1.0', 1);

                INSERT INTO works (id, media_type, work_kind)
                    VALUES ($workId, 'Movies', 'standalone');
                INSERT INTO editions (id, work_id)
                    VALUES ($editionId, $workId);
                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                    VALUES ($assetId, $editionId, 'arrival-test-hash', 'C:/library/Arrival.mkv');

                INSERT INTO persons (id, name, created_at)
                    VALUES ($personId, 'Jeremy Renner', $now);

                INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
                    VALUES ($claim1, $workId, $providerId, 'cast_member', 'Amy Adams', 0.90, $now);
                INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
                    VALUES ($claim2, $workId, $providerId, 'cast_member_character', 'Dr. Louise Banks', 0.90, $now);
                INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
                    VALUES ($claim3, $workId, $providerId, 'cast_member', 'Jeremy Renner', 0.90, $now);
                INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
                    VALUES ($claim4, $workId, $providerId, 'cast_member_character', 'Ian Donnelly', 0.90, $now);
                """;
            cmd.Parameters.AddWithValue("$providerId", providerId.ToString("D"));
            cmd.Parameters.AddWithValue("$workId", workId.ToString("D"));
            cmd.Parameters.AddWithValue("$editionId", editionId.ToString("D"));
            cmd.Parameters.AddWithValue("$assetId", assetId.ToString("D"));
            cmd.Parameters.AddWithValue("$personId", personId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$claim1", Guid.NewGuid().ToString("D"));
            cmd.Parameters.AddWithValue("$claim2", Guid.NewGuid().ToString("D"));
            cmd.Parameters.AddWithValue("$claim3", Guid.NewGuid().ToString("D"));
            cmd.Parameters.AddWithValue("$claim4", Guid.NewGuid().ToString("D"));
            cmd.ExecuteNonQuery();
        }

        var credits = await CastCreditQueries.BuildForWorkAsync(
            workId,
            new CanonicalValueArrayRepository(_db),
            new PersonRepository(_db),
            _db,
            CancellationToken.None);

        var jeremy = Assert.Single(credits, credit => credit.Name == "Jeremy Renner");
        Assert.Equal(personId, jeremy.PersonId);
        Assert.Equal("Ian Donnelly", Assert.Single(jeremy.Characters).CharacterName);
    }
}
