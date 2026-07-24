using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Domain;
using MediaEngine.Storage;

namespace MediaEngine.Api.Tests;

public sealed class PersonCreditReadServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public PersonCreditReadServiceTests()
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
    public async Task GetLibraryCreditsAsync_CollapsesTvEpisodesToSeriesAndPreservesEveryRole()
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
            AddGuid(cmd, "$collectionId", collectionId);
            AddGuid(cmd, "$personId", personId);
            AddGuid(cmd, "$characterId", characterId);
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
                    INSERT INTO canonical_value_arrays (entity_id, key, ordinal, value)
                        VALUES ($workId, 'director', 0, 'Bryan Test Person');
                    INSERT INTO canonical_value_arrays (entity_id, key, ordinal, value)
                        VALUES ($workId, 'cast_member', 0, 'Bryan Test Person');
                    """;
                AddGuid(episodeCmd, "$workId", workId);
                AddGuid(episodeCmd, "$collectionId", collectionId);
                episodeCmd.Parameters.AddWithValue("$workQid", $"QEP{episode}");
                episodeCmd.Parameters.AddWithValue("$episode", episode);
                AddGuid(episodeCmd, "$editionId", editionId);
                AddGuid(episodeCmd, "$assetId", assetId);
                episodeCmd.Parameters.AddWithValue("$hash", $"sample-crime-show-{episode}");
                episodeCmd.Parameters.AddWithValue("$path", $"C:/library/Sample Crime Show/S01E0{episode}.mkv");
                episodeCmd.Parameters.AddWithValue("$title", $"Episode {episode}");
                AddGuid(episodeCmd, "$personId", personId);
                episodeCmd.Parameters.AddWithValue("$now", now);
                episodeCmd.ExecuteNonQuery();
            }
        }

        var service = CreateService();
        var credits = await service.GetLibraryCreditsAsync(personId, CancellationToken.None);

        Assert.Equal(2, credits.Count);
        Assert.All(credits, credit =>
        {
            Assert.Equal(collectionId, credit.CollectionId);
            Assert.Equal("Sample Crime Show", credit.Title);
        });
        var actorCredit = Assert.Single(credits, credit => credit.Role == "Actor");
        Assert.Single(actorCredit.Characters);
        Assert.Equal("Lead Chemistry Teacher", actorCredit.Characters[0].CharacterName);
        Assert.Single(credits, credit => credit.Role == "Director");
    }

    [Fact]
    public async Task GetLibraryCreditsAsync_PreservesMultipleRolesForOneOwnedWork()
    {
        var personId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");

        using (var conn = _db.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO persons (id, name, created_at)
                    VALUES ($personId, 'Test Musician', $now);
                INSERT INTO works (id, media_type, work_kind)
                    VALUES ($workId, 'Music', 'standalone');
                INSERT INTO editions (id, work_id)
                    VALUES ($editionId, $workId);
                INSERT INTO media_assets (id, edition_id, content_hash, file_path_root)
                    VALUES ($assetId, $editionId, 'multi-role-album', 'C:/library/Multi Role Album.flac');
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
                    VALUES ($workId, 'title', 'Multi Role Album', $now);
                INSERT INTO canonical_value_arrays (entity_id, key, ordinal, value)
                    VALUES ($workId, 'artist', 0, 'Test Musician');
                INSERT INTO canonical_value_arrays (entity_id, key, ordinal, value)
                    VALUES ($workId, 'composer', 0, 'Test Musician');
                """;
            AddGuid(cmd, "$personId", personId);
            AddGuid(cmd, "$workId", workId);
            AddGuid(cmd, "$editionId", editionId);
            AddGuid(cmd, "$assetId", assetId);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        var service = CreateService();
        var credits = await service.GetLibraryCreditsAsync(personId, CancellationToken.None);

        Assert.Equal(["Artist", "Composer"], credits.Select(credit => credit.Role).Order());
        Assert.Single(credits.Select(credit => credit.WorkId).Distinct());
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
                INSERT INTO person_media_links (media_asset_id, person_id, role)
                    VALUES ($assetId, $personId, 'Actor');

                INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
                    VALUES ($claim1, $workId, $providerId, 'cast_member', 'Amy Adams', 0.90, $now);
                INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
                    VALUES ($claim2, $workId, $providerId, 'cast_member_character', 'Dr. Louise Banks', 0.90, $now);
                INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
                    VALUES ($claim3, $workId, $providerId, 'cast_member', 'Jeremy Renner', 0.90, $now);
                INSERT INTO metadata_claims (id, entity_id, provider_id, claim_key, claim_value, confidence, claimed_at)
                    VALUES ($claim4, $workId, $providerId, 'cast_member_character', 'Ian Donnelly', 0.90, $now);
                """;
            AddGuid(cmd, "$providerId", providerId);
            AddGuid(cmd, "$workId", workId);
            AddGuid(cmd, "$editionId", editionId);
            AddGuid(cmd, "$assetId", assetId);
            AddGuid(cmd, "$personId", personId);
            cmd.Parameters.AddWithValue("$now", now);
            AddGuid(cmd, "$claim1", Guid.NewGuid());
            AddGuid(cmd, "$claim2", Guid.NewGuid());
            AddGuid(cmd, "$claim3", Guid.NewGuid());
            AddGuid(cmd, "$claim4", Guid.NewGuid());
            cmd.ExecuteNonQuery();
        }

        var service = CreateService();
        var credits = await service.BuildForWorkAsync(workId, CancellationToken.None);

        var jeremy = Assert.Single(credits, credit => credit.Name == "Jeremy Renner");
        Assert.Equal(personId, jeremy.PersonId);
        Assert.Equal("Ian Donnelly", Assert.Single(jeremy.Characters).CharacterName);
    }

    [Fact]
    public async Task GetGroupMembersAsync_ReturnsMembersAndParentGroupsInNameOrder()
    {
        var groupId = Guid.NewGuid();
        var firstMemberId = Guid.NewGuid();
        var secondMemberId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");

        using (var conn = _db.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO persons (id, name, is_group, created_at)
                    VALUES ($groupId, 'The Test Group', 1, $now);
                INSERT INTO persons (id, name, created_at)
                    VALUES ($firstMemberId, 'Zed Member', $now);
                INSERT INTO persons (id, name, created_at)
                    VALUES ($secondMemberId, 'Amy Member', $now);
                INSERT INTO person_group_members (group_id, member_id)
                    VALUES ($groupId, $firstMemberId);
                INSERT INTO person_group_members (group_id, member_id)
                    VALUES ($groupId, $secondMemberId);
                """;
            AddGuid(cmd, "$groupId", groupId);
            AddGuid(cmd, "$firstMemberId", firstMemberId);
            AddGuid(cmd, "$secondMemberId", secondMemberId);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        var service = CreateService();

        var members = await service.GetGroupMembersAsync(groupId, isGroup: true, CancellationToken.None);
        var groups = await service.GetGroupMembersAsync(firstMemberId, isGroup: false, CancellationToken.None);

        Assert.Equal(["Amy Member", "Zed Member"], members.Select(member => member.Name));
        Assert.Equal("The Test Group", Assert.Single(groups).Name);
    }

    [Fact]
    public async Task GetLibraryCreditsAsync_ReturnsEmptyListWhenPersonHasNoCredits()
    {
        var service = CreateService();

        var credits = await service.GetLibraryCreditsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(credits);
    }

    private PersonCreditReadService CreateService() =>
        new(new CanonicalValueArrayRepository(_db), new PersonRepository(_db), _db);

    private static void AddGuid(Microsoft.Data.Sqlite.SqliteCommand command, string name, Guid value)
    {
        command.Parameters.AddWithValue(name, GuidSql.ToBlob(value));
    }
}
