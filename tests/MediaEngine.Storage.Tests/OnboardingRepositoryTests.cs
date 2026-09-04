using MediaEngine.Storage;

namespace MediaEngine.Storage.Tests;

public sealed class OnboardingRepositoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"tuvima-onboarding-{Guid.NewGuid():N}.db");
    private readonly DatabaseConnection _database;
    private readonly OnboardingRepository _repository;

    public OnboardingRepositoryTests()
    {
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
        _database.RunStartupChecks();
        _repository = new OnboardingRepository(_database);
    }

    [Fact]
    public async Task SetupStartAndStepWritesAreDurableAndIdempotent()
    {
        var expires = DateTimeOffset.UtcNow.AddHours(1);

        Assert.True(await _repository.TryBeginAsync("session-hash", Guid.NewGuid(), expires, CancellationToken.None));
        Assert.True(await _repository.TryBeginAsync("other-hash", Guid.NewGuid(), expires, CancellationToken.None));

        await _repository.SetStepAsync("preflight", "passed", "Ready.", null, null, CancellationToken.None);
        await _repository.SetStepAsync("preflight", "passed", "Ready.", null, null, CancellationToken.None);

        var workflow = _repository.Get();
        Assert.Equal(1, workflow.WorkflowVersion);
        Assert.Equal("administrator", workflow.CurrentStep);
        Assert.Equal("passed", workflow.Steps.Single(step => step.Key == "preflight").Status);
        Assert.True(await _repository.ValidateSessionAsync("session-hash", CancellationToken.None));
        Assert.True(await _repository.ValidateSessionAsync("other-hash", CancellationToken.None));
    }

    [Fact]
    public async Task CompletionRequiresEveryRequiredCapabilityButAllowsOptionalDeferrals()
    {
        Assert.True(await _repository.TryBeginAsync(
            "session-hash", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None));
        await _repository.SetStepAsync("preflight", "passed", null, null, null, CancellationToken.None);
        await _repository.SetStepAsync("administrator", "passed", null, null, null, CancellationToken.None);
        await _repository.SetStepAsync("providers", "deferred", null, "/setup?step=providers", null, CancellationToken.None);
        await _repository.SetStepAsync("local-ai", "deferred", null, "/setup?step=local-ai", null, CancellationToken.None);
        await _repository.SetStepAsync("access", "deferred", null, "/setup?step=access", null, CancellationToken.None);

        Assert.False(await _repository.CompleteAsync(CancellationToken.None));

        await _repository.SetStepAsync("media-locations", "passed", null, null, null, CancellationToken.None);
        Assert.True(await _repository.CompleteAsync(CancellationToken.None));
        Assert.Equal("complete", _repository.Get().State);
        Assert.False(await _repository.ValidateSessionAsync("session-hash", CancellationToken.None));
    }

    public void Dispose()
    {
        _database.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }
}
