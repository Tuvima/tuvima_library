using System.Net;
using System.Text.Json;
using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.Display;
using MediaEngine.Api.Services.Metadata;
using MediaEngine.Domain.Authorization;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MediaEngine.Api.Tests;

/// <summary>Exercises the mapped editor endpoints with their real repositories and resource admission path.</summary>
public sealed class SharedEntityEditorEndpointIntegrationTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"tuvima_shared_editor_{Guid.NewGuid():N}.db");
    private readonly DatabaseConnection _database;

    public SharedEntityEditorEndpointIntegrationTests()
    {
        DapperConfiguration.Configure();
        _database = new DatabaseConnection(_databasePath);
        _database.InitializeSchema();
    }

    [Fact]
    public async Task MappedRoutes_DenyRawRootWithoutOwnedProvenance_AndPageOnlyVisibleEntities()
    {
        var libraryId = Guid.NewGuid();
        var workId = Guid.NewGuid();
        var editionId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var eventOne = Guid.NewGuid();
        var eventTwo = Guid.NewGuid();
        await using var connection = _database.CreateConnection();
        await connection.ExecuteAsync("""
            INSERT INTO narrative_roots (qid, label, level, created_at)
            VALUES ('Q-owned-root', 'Owned universe', 'Universe', datetime('now')),
                   ('Q-raw-only', 'Raw only universe', 'Universe', datetime('now'));
            INSERT INTO works (id, media_type, work_kind, ownership, wikidata_qid)
            VALUES (@workId, 'Books', 'standalone', 'Owned', 'Q-owned-work');
            INSERT INTO editions (id, work_id) VALUES (@editionId, @workId);
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, library_id)
            VALUES (@assetId, @editionId, 'shared-editor-route', 'books/shared-editor.epub', @libraryId);
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at)
            VALUES (@workId, 'narrative_root_qid', 'Q-owned-root', datetime('now')),
                   (@eventOne, 'point_in_time', '+0019-01-01T00:00:00Z', datetime('now')),
                   (@eventOne, 'time_index', 'battle-12', datetime('now'));
            INSERT INTO fictional_entities (id, wikidata_qid, label, entity_sub_type, fictional_universe_qid, created_at)
            VALUES (@eventOne, 'Q-event-one', 'First event', 'Event', 'Q-owned-root', datetime('now')),
                   (@eventTwo, 'Q-event-two', 'Second event', 'Event', 'Q-owned-root', datetime('now'));
            INSERT INTO fictional_entity_work_links (id, appearance_key, entity_id, work_qid, provenance)
            VALUES (randomblob(16), 'event-one-work', @eventOne, 'Q-owned-work', 'Wikidata'),
                   (randomblob(16), 'event-two-work', @eventTwo, 'Q-owned-work', 'Wikidata');
            """, new { workId, editionId, assetId, libraryId = libraryId.ToString("D"), eventOne, eventTwo });

        await using var app = await StartAsync(libraryId, workId);
        using var client = new HttpClient { BaseAddress = app.Address };

        using var raw = await client.GetAsync("/entity-editor/universes/Q-raw-only/context");
        using var context = await client.GetAsync("/entity-editor/universes/Q-owned-root/context");
        using var page = await client.GetAsync("/entity-editor/universes/Q-owned-root/entities?category=Event&offset=0&limit=1");

        Assert.True(raw.StatusCode == HttpStatusCode.NotFound, await raw.Content.ReadAsStringAsync());
        Assert.True(context.StatusCode == HttpStatusCode.OK, await context.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
        Assert.True(json.RootElement.GetProperty("has_more").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("items").GetArrayLength());
    }

    private async Task<TestApplication> StartAsync(Guid libraryId, Guid workId)
    {
        var authority = new RequestAuthority(
            PrincipalKind.DelegatedUserClient,
            true,
            AccountId: Guid.NewGuid(),
            ActiveProfileId: Guid.NewGuid(),
            ApplicationId: Guid.NewGuid(),
            DeviceId: Guid.NewGuid(),
            AccountEnabled: true,
            GrantEnabled: true,
            ApplicationEnabled: true,
            AccountAuthorizationVersion: 1,
            GrantAuthorizationVersion: 1,
            ApplicationAuthorizationVersion: 1);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorization(options => options.AddPolicy(AuthPolicies.Authenticated, policy => policy.RequireAssertion(_ => true)));
        builder.Services.AddSingleton<IDatabaseConnection>(_database);
        builder.Services.AddSingleton<INarrativeRootRepository, NarrativeRootRepository>();
        builder.Services.AddSingleton<IFictionalEntityRepository, FictionalEntityRepository>();
        builder.Services.AddSingleton<IEntityRelationshipRepository, EntityRelationshipRepository>();
        builder.Services.AddSingleton<ICanonicalValueRepository, CanonicalValueRepository>();
        builder.Services.AddSingleton<IEntityAssetRepository, EntityAssetRepository>();
        builder.Services.AddSingleton<IEntityTimelineRepository, EntityTimelineRepository>();
        builder.Services.AddSingleton<IPluginLoreRepository, PluginLoreRepository>();
        builder.Services.AddSingleton<IMetadataHarvestingService, RecordingHarvestingService>();
        builder.Services.AddSingleton<ArtworkScopeService>(_ => null!);
        builder.Services.AddSingleton<IDisplayProjectionReadService>(new DisplayStub([new DisplayWorkRow { WorkId = workId, IdentityQid = "Q-owned-work" }]));
        builder.Services.AddSingleton<IRequestAuthorityResolver>(new FixedAuthorityResolver(authority));
        builder.Services.AddSingleton<IAccountAccessDecisionService>(new LibraryAccess(libraryId));
        builder.Services.AddSingleton<IAuthorizationEvaluator, AllowAuthorizationEvaluator>();
        builder.Services.AddScoped<CatalogueResourceAuthorizationService>();
        var app = builder.Build();
        app.UseDeveloperExceptionPage();
        app.UseAuthorization();
        app.MapSharedEntityEditorEndpoints();
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return new TestApplication(app, new Uri(addresses!.Addresses.Single()));
    }

    public void Dispose()
    {
        try { _database.Dispose(); } catch { }
        try { File.Delete(_databasePath); } catch { }
    }

    private sealed class DisplayStub(IReadOnlyList<DisplayWorkRow> works) : IDisplayProjectionReadService
    {
        public Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct) => Task.FromResult(works);
        public Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct) => Task.FromResult<IReadOnlyList<DisplayJourneyRow>>([]);
        public Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct) => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
        public Task<IReadOnlyList<DisplayHomeCollectionRow>> LoadHomeCollectionsAsync(Guid? profileId, CancellationToken ct) => Task.FromResult<IReadOnlyList<DisplayHomeCollectionRow>>([]);
    }

    private sealed class FixedAuthorityResolver(RequestAuthority authority) : IRequestAuthorityResolver
    {
        public ValueTask<RequestAuthority> ResolveAsync(HttpContext context, CancellationToken ct = default) => ValueTask.FromResult(authority);
    }

    private sealed class LibraryAccess(Guid libraryId) : IAccountAccessDecisionService
    {
        public ValueTask<AuthorizationDecision> EvaluateFeatureAsync(RequestAuthority authority, AccountFeatureId feature, CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationDecision.Allow());
        public ValueTask<AuthorizationDecision> EvaluateLibraryAsync(RequestAuthority authority, Guid requestedLibraryId, CancellationToken cancellationToken = default) => ValueTask.FromResult(requestedLibraryId == libraryId ? AuthorizationDecision.Allow() : AuthorizationDecision.Deny(AuthorizationDenialReason.MissingLibraryGrant));
        public ValueTask<AuthorizationDecision> EvaluateAdministratorAsync(RequestAuthority authority, bool requireSurfaceUnlock, CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationDecision.Allow());
    }

    private sealed class AllowAuthorizationEvaluator : IAuthorizationEvaluator
    {
        public ValueTask<AuthorizationDecision> EvaluateAsync(RequestAuthority authority, AuthorizationRequirement requirement, ResourceAuthorizationContext? resource, CancellationToken cancellationToken = default) => ValueTask.FromResult(AuthorizationDecision.Allow());
    }

    private sealed class RecordingHarvestingService : IMetadataHarvestingService
    {
        public int PendingCount => 0;
        public ValueTask EnqueueAsync(HarvestRequest request, CancellationToken ct = default) => ValueTask.CompletedTask;
        public Task ProcessSynchronousAsync(HarvestRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class TestApplication(WebApplication app, Uri address) : IAsyncDisposable
    {
        public Uri Address { get; } = address;
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
