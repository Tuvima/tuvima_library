using Bunit;
using MediaEngine.Contracts.Display;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Components.Settings;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Discovery;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Theming;
using MediaEngine.Web.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using MudBlazor;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class Phase1SetupReadinessTests : TestContext
{
    public Phase1SetupReadinessTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string>("detectDeviceClass").SetResult("web");

        Services.AddLocalization();
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Engine:BaseUrl"] = "http://localhost:61495",
            })
            .Build());
        Services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());
        Services.AddScoped<DeviceContextService>();
        Services.AddScoped<UniverseStateContainer>();
        Services.AddScoped<ActiveProfileSessionService>();
        Services.AddScoped<UIOrchestratorService>();
        Services.AddScoped<SetupReadinessService>();
        Services.AddScoped<DiscoveryComposerService>();
    }

    [Fact]
    public void SetupPage_RendersReadinessChecklist()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.CreateDefault());

        var cut = RenderComponent<SetupTab>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Setup checklist", cut.Markup);
            Assert.Contains("Engine connection", cut.Markup);
            Assert.Contains("Folders", cut.Markup);
            Assert.Contains("Providers", cut.Markup);
            Assert.Contains("Local AI", cut.Markup);
            Assert.Contains("Scan and ingestion", cut.Markup);
            Assert.Contains("Review Queue", cut.Markup);
        });
    }

    [Fact]
    public void SetupPage_ShowsFolderActionsWhenFoldersMissing()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetFolderSettingsAsync),
                _ => Task.FromResult<FolderSettingsDto?>(new FolderSettingsDto("", "")));
        }));

        var cut = RenderComponent<SetupTab>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Choose where organized media should live", cut.Markup);
            Assert.Contains("/settings/folders", cut.Markup);
            Assert.Contains("Configure folders", cut.Markup);
        });
    }

    [Fact]
    public void SetupPage_DoesNotUseFakeProviderFallback()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.GetProviderStatusAsync),
                _ => Task.FromResult<IReadOnlyList<ProviderStatusDto>>([]));
        }));

        var cut = RenderComponent<SetupTab>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No metadata providers are enabled yet", cut.Markup);
            Assert.DoesNotContain("Apple API", cut.Markup);
            Assert.DoesNotContain("MusicBrainz", cut.Markup);
            Assert.DoesNotContain("Fanart.tv", cut.Markup);
        });
    }

    [Fact]
    public void SetupPage_ShowsFolderReadinessStateFromMockedApiData()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.TestPathAsync),
                args =>
                {
                    var path = args?[0]?.ToString() ?? string.Empty;
                    var canWrite = path.Contains("Library", StringComparison.OrdinalIgnoreCase);
                    return Task.FromResult<PathTestResultDto?>(new PathTestResultDto(path, true, true, canWrite));
                });
        }));

        var cut = RenderComponent<SetupTab>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("The Engine can read this folder but cannot write to it.", cut.Markup);
            Assert.Contains("C:\\Tuvima\\Incoming", cut.Markup);
        });
    }

    [Fact]
    public void HomeEmptyState_LinksToSetupAndFolderSettings()
    {
        var source = File.ReadAllText(GetRepoFile(
            "src", "MediaEngine.Web", "Components", "Pages", "LibraryBrowsePage.razor"));

        Assert.Contains("Open setup checklist", source);
        Assert.Contains("Href=\"/settings/setup\"", source);
        Assert.Contains("Configure folders", source);
        Assert.Contains("Href=\"/settings/folders\"", source);
        Assert.Contains("Open Library Operations", source);
        Assert.Contains("Href=\"/settings/ingestion\"", source);
        Assert.Contains("_readiness?.CanScan == true", source);
        Assert.Contains("Orchestrator.TriggerRescanAsync", source);
    }

    [Fact]
    public void SetupPage_LinksToLibraryOperationsAfterScan()
    {
        var source = File.ReadAllText(GetRepoFile(
            "src", "MediaEngine.Web", "Components", "Settings", "SetupTab.razor"));

        Assert.Contains("Orchestrator.TriggerRescanAsync", source);
        Assert.Contains("Open Library Operations", source);
        Assert.Contains("/settings/ingestion", source);
        Assert.Contains("Scan request sent", source);
    }

    private static string GetRepoFile(params string[] segments) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", ".." }.Concat(segments).ToArray()));

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MediaEngine.Web.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    }
}
