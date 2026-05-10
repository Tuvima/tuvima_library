using Bunit;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Theming;
using MediaEngine.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class Phase8AdvancedIntelligenceTests : TestContext
{
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public Phase8AdvancedIntelligenceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLocalization();
        Services.AddLogging();
        Services.AddMudServices();
    }

    [Fact]
    public void Recommendations_DoNotRenderFakeItems()
    {
        var endpoint = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "MediaEngine.Api",
            "Endpoints",
            "CollectionEndpoints.cs"));

        Assert.DoesNotContain("OrderBy(_ => Guid.NewGuid())", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("Explore Your Library", endpoint, StringComparison.Ordinal);
        Assert.Contains("Same Series", endpoint, StringComparison.Ordinal);
        Assert.Contains("Same Creator", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void ChronicleExplorer_RendersGraphData()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetUniverseGraphAsync), _ =>
                Task.FromResult<UniverseGraphResponse?>(new UniverseGraphResponse
                {
                    Universe = new UniverseInfo { Qid = "Q1", Label = "Dune" },
                    Nodes =
                    [
                        new GraphNodeDto { Id = "Q10", Label = "Paul Atreides", Type = "Character" },
                        new GraphNodeDto { Id = "Q11", Label = "Arrakis", Type = "Location" },
                    ],
                    Edges =
                    [
                        new GraphEdgeDto { Source = "Q10", Target = "Q11", Type = "residence", Label = "resides in" },
                    ],
                }));
            stub.SetHandler(nameof(IEngineApiClient.CheckLoreDeltaAsync), _ =>
                Task.FromResult<IReadOnlyList<LoreDeltaResultDto>>([]));
        }));
        Services.AddScoped<DeviceContextService>();

        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<ChronicleExplorer>(1);
            builder.AddAttribute(2, nameof(ChronicleExplorer.Qid), "Q1");
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Dune", cut.Markup);
            Assert.Contains("2 entities", cut.Markup);
            Assert.Contains("1 relationships", cut.Markup);
            Assert.DoesNotContain("Universe Graph unavailable", cut.Markup);
        });
    }

    [Fact]
    public async Task ChronicleExplorer_DisabledOnMobile()
    {
        var api = EngineApiClientStub.CreateDefault();
        Services.AddSingleton<IEngineApiClient>(api);
        Services.AddScoped<DeviceContextService>();
        var device = Services.GetRequiredService<DeviceContextService>();
        await device.SwitchDeviceAsync("mobile");

        var cut = RenderComponent<ChronicleExplorer>(parameters => parameters
            .Add(component => component.Qid, "Q1"));

        Assert.Contains("Chronicle Explorer is unavailable on mobile", cut.Markup);
        Assert.DoesNotContain("cytoscape-container", cut.Markup);
    }

    [Fact]
    public void ChronicleExplorer_RendersGraphEmptyState()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetUniverseGraphAsync), _ =>
                Task.FromResult<UniverseGraphResponse?>(new UniverseGraphResponse
                {
                    Universe = new UniverseInfo { Qid = "Q1", Label = "Unenriched Universe" },
                }));
            stub.SetHandler(nameof(IEngineApiClient.CheckLoreDeltaAsync), _ =>
                Task.FromResult<IReadOnlyList<LoreDeltaResultDto>>([]));
        }));
        Services.AddScoped<DeviceContextService>();

        var cut = RenderComponent<ChronicleExplorer>(parameters => parameters
            .Add(component => component.Qid, "Q1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Universe Graph unavailable", cut.Markup);
            Assert.DoesNotContain("cytoscape-container", cut.Markup);
        });
    }
}
