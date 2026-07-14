using Dapper;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Services;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage.Tests;

public sealed class ProfileRepositoryInvariantTests : IDisposable
{
    private readonly string _databasePath;
    private readonly DatabaseConnection _database;
    private readonly ProfileRepository _repository;

    public ProfileRepositoryInvariantTests()
    {
        DapperConfiguration.Configure();
        _databasePath = Path.Combine(Path.GetTempPath(), $"tuvima_profile_invariants_{Guid.NewGuid():N}.db");
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _repository = new ProfileRepository(_database);
    }

    [Fact]
    public async Task UpdateAsync_RejectsSeedOwnerDemotionAtomically()
    {
        var owner = Assert.IsType<Profile>(await _repository.GetByIdAsync(Profile.SeedProfileId));
        owner.Role = ProfileRole.Consumer;

        var updated = await _repository.UpdateAsync(owner);

        Assert.False(updated);
        var persisted = Assert.IsType<Profile>(await _repository.GetByIdAsync(Profile.SeedProfileId));
        Assert.Equal(ProfileRole.Administrator, persisted.Role);
    }

    [Fact]
    public async Task UpdateAsync_RejectsDemotionWhenNoOtherAdministratorExists()
    {
        using (var connection = _database.CreateConnection())
        {
            connection.Execute("DELETE FROM profiles WHERE id = @id;", new { id = Profile.SeedProfileId });
        }

        var administrator = CreateProfile(ProfileRole.Administrator);
        await _repository.InsertAsync(administrator);
        administrator.Role = ProfileRole.Curator;

        var updated = await _repository.UpdateAsync(administrator);

        Assert.False(updated);
        var persisted = Assert.IsType<Profile>(await _repository.GetByIdAsync(administrator.Id));
        Assert.Equal(ProfileRole.Administrator, persisted.Role);
    }

    [Fact]
    public async Task UpdateAsync_AllowsDemotionWhenAnotherAdministratorRemains()
    {
        var administrator = CreateProfile(ProfileRole.Administrator);
        await _repository.InsertAsync(administrator);
        administrator.Role = ProfileRole.Curator;

        var updated = await _repository.UpdateAsync(administrator);

        Assert.True(updated);
        var persisted = Assert.IsType<Profile>(await _repository.GetByIdAsync(administrator.Id));
        Assert.Equal(ProfileRole.Curator, persisted.Role);
        var owner = Assert.IsType<Profile>(await _repository.GetByIdAsync(Profile.SeedProfileId));
        Assert.Equal(ProfileRole.Administrator, owner.Role);
    }

    public void Dispose()
    {
        _database.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }

    private static Profile CreateProfile(ProfileRole role) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = $"{role} Profile",
        AvatarColor = "#7C4DFF",
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
