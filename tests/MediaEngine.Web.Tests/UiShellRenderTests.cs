using Bunit;
using MediaEngine.Web.Components.Collections;
using MediaEngine.Web.Components.Library;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Discovery;
using MediaEngine.Web.Services.Editing;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Playback;
using MediaEngine.Web.Services.Theming;
using MediaEngine.Web.Shared;
using MediaEngine.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class UiShellRenderTests : TestContext
{
    public UiShellRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string>("detectDeviceClass").SetResult("web");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Engine:BaseUrl"] = "http://localhost:61495",
            })
            .Build();

        var apiClient = EngineApiClientStub.CreateDefault();

        Services.AddLocalization();
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddSingleton<IConfiguration>(configuration);
        Services.AddSingleton<IEngineApiClient>(apiClient);
        Services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());
        Services.AddSingleton<ThemeService>();
        Services.AddScoped<DeviceContextService>();
        Services.AddScoped<UniverseStateContainer>();
        Services.AddScoped<UIOrchestratorService>();
        Services.AddScoped<CollectionEditorLauncherService>();
        Services.AddScoped<DiscoveryComposerService>();
        Services.AddScoped<ListenPlaybackService>();
        Services.AddScoped<MediaReactionService>();
        Services.AddScoped<MediaEditorLauncherService>();
    }

    [Fact]
    public void MainLayout_RendersMudShellAndBody()
    {
        var cut = RenderComponent<MainLayout>(parameters => parameters
            .Add(layout => layout.Body, builder => builder.AddMarkupContent(0, "<section id=\"test-body\">Body content</section>")));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".mud-appbar"));
            Assert.Equal(3, cut.FindAll(".layout-shell__nav-link").Count);
            Assert.Empty(cut.FindAll(".layout-shell__mobile-menu"));
            Assert.Contains("Body content", cut.Markup);
            Assert.DoesNotContain("Home", cut.Markup);
            Assert.Contains("Search your library", cut.Markup);
            Assert.Single(cut.FindAll(".layout-shell__search-shell"));
            Assert.Single(cut.FindAll(".layout-shell__profile-trigger"));
            Assert.Empty(cut.FindAll(".layout-shell__avatar-trigger"));
        });
    }

    [Fact]
    public void SettingsPage_RendersSharedSidebarAndSystemContent()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<MudSnackbarProvider>(2);
            builder.CloseComponent();
            builder.OpenComponent<Settings>(3);
            builder.AddAttribute(4, nameof(Settings.Section), "system");
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".sidebar-page"));
            Assert.Single(cut.FindAll(".sidebar-rail"));
            Assert.Empty(cut.FindAll(".mud-tabs"));
            Assert.Contains("Runtime status", cut.Markup);
            Assert.Contains("Server identity and regional defaults", cut.Markup);
        });
    }

    [Fact]
    public void SettingsPage_RendersUserOverviewWithoutAdminOverviewContent()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<MudSnackbarProvider>(2);
            builder.CloseComponent();
            builder.OpenComponent<Settings>(3);
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Personal overview", cut.Markup);
            Assert.Contains("Your Activity", cut.Markup);
            Assert.Contains("Pick up where you left off", cut.Markup);
            Assert.Contains("Library habits", cut.Markup);
            Assert.Contains("Personal timeline", cut.Markup);
            Assert.DoesNotContain("Runtime status", cut.Markup);
            Assert.DoesNotContain("Heartbeat", cut.Markup);
            Assert.DoesNotContain("Stage 3 coverage", cut.Markup);
        });
    }

    [Fact]
    public void SettingsReviewPage_RendersDedicatedReviewList()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<MudSnackbarProvider>(2);
            builder.CloseComponent();
            builder.OpenComponent<Settings>(3);
            builder.AddAttribute(4, nameof(Settings.Section), "review");
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Review Queue", cut.Markup);
            Assert.Contains("Unmatched Album", cut.Markup);
            Assert.Contains("Skip Universe", cut.Markup);
        });
    }

    [Fact]
    public void CollectionsPage_RendersMudTabsAndTableRows()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<MudSnackbarProvider>(2);
            builder.CloseComponent();
            builder.OpenComponent<CollectionsPage>(3);
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".mud-tabs"));
            Assert.Single(cut.FindAll(".mud-table"));
            Assert.Contains("Summer Movies", cut.Markup);
            Assert.Contains("Quiet Reads", cut.Markup);
        });
    }

    [Fact]
    public void LibraryConfigurableTable_RendersMudTableShellAndMudActions()
    {
        var cut = RenderComponent<LibraryConfigurableTable>(parameters => parameters
            .Add(component => component.Columns, LibraryColumnDefinitions.GetColumnsByTab("books"))
            .Add(component => component.Items, [CreateSampleLibraryItem()])
            .Add(component => component.Loading, false)
            .Add(component => component.SelectedItems, new HashSet<Guid>()));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".mud-simple-table"));
            Assert.Contains("The Hobbit", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".mud-button-root"));
        });
    }

    [Fact]
    public void LibraryColumnPicker_RendersMudCheckboxesAndActions()
    {
        var columns = LibraryColumnDefinitions.GetColumnsByTab("books");
        var visibleKeys = columns.Where(column => column.DefaultVisible).Select(column => column.Key).ToList();

        var cut = RenderComponent<LibraryColumnPicker>(parameters => parameters
            .Add(component => component.AllColumns, columns)
            .Add(component => component.VisibleKeys, visibleKeys)
            .Add(component => component.ViewKey, "books"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Columns", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".mud-checkbox"));
            Assert.Contains("Reset to Defaults", cut.Markup);
        });
    }

    [Fact]
    public void LibraryDeleteConfirm_RendersMudConfirmActions()
    {
        var cut = RenderComponent<LibraryDeleteConfirm>(parameters => parameters
            .Add(component => component.Count, 2));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Move 2 Items to Quarantine?", cut.Markup);
            Assert.Equal(2, cut.FindAll(".mud-button-root").Count);
            Assert.Contains("Quarantine", cut.Markup);
        });
    }

    [Fact]
    public void ListenPage_RendersPermanentRailWithoutDrawerControls()
    {
        var cut = RenderComponent<ListenPage>(parameters => parameters
            .Add(page => page.Section, "songs"));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".sidebar-page"));
            Assert.Single(cut.FindAll(".listen-rail"));
            Assert.Empty(cut.FindAll(".listen-topbar__menu"));
            Assert.Empty(cut.FindAll(".listen-rail__close"));
            Assert.DoesNotContain("Pins", cut.Markup);
        });
    }

    [Fact]
    public void ListenPage_RendersMusicQuickAccessAndPlaylistsInOrder()
    {
        var cut = RenderComponent<ListenPage>(parameters => parameters
            .Add(page => page.Section, "songs"));

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            var music = markup.IndexOf(">Music<", StringComparison.Ordinal);
            var quickAccess = markup.IndexOf(">Quick access<", StringComparison.Ordinal);
            var playlists = markup.IndexOf(">Playlists<", StringComparison.Ordinal);

            Assert.True(music >= 0, "Music section should render.");
            Assert.True(quickAccess > music, "Quick access should render below Music.");
            Assert.True(playlists > quickAccess, "Playlists should render below Quick access.");
            Assert.Single(cut.FindAll(".listen-rail__section-toggle"));
            Assert.Contains("Summer Movies", markup);
            Assert.Contains("Add playlist", markup);
            Assert.Contains("Drag to reorder Summer Movies", markup);
            Assert.DoesNotContain("Edit playlist", markup);
            Assert.DoesNotContain("All Songs", markup);
        });
    }

    [Fact]
    public void SearchPage_RendersMudSearchResultsWithoutRawCards()
    {
        var state = Services.GetRequiredService<UniverseStateContainer>();
        state.SetCollections([
            CollectionViewModel.FromApiDto(
                id: Guid.Parse("30000000-0000-0000-0000-000000000001"),
                universeId: null,
                createdAt: DateTimeOffset.UtcNow,
                works:
                [
                    new WorkViewModel
                    {
                        Id = Guid.Parse("30000000-0000-0000-0000-000000000101"),
                        CollectionId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                        MediaType = "Book",
                        CreatedAt = DateTimeOffset.UtcNow,
                        CanonicalValues =
                        [
                            new CanonicalValueViewModel
                            {
                                Key = "title",
                                Value = "Dune",
                                LastScoredAt = DateTimeOffset.UtcNow,
                            },
                        ],
                    },
                ],
                displayName: "Dune")
        ]);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameter("q", "dune"));

        var cut = RenderComponent<SearchPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Dune", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".search-recent-grid"));
            Assert.NotEmpty(cut.FindAll(".mud-paper"));
            Assert.NotEmpty(cut.FindAll(".mud-link"));
        });
    }

    [Fact]
    public void PersonDetail_RendersNotFoundStateWithMudActions()
    {
        var cut = RenderComponent<PersonDetail>(parameters => parameters
            .Add(page => page.Id, Guid.Parse("40000000-0000-0000-0000-000000000001")));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Person not found.", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".mud-alert"));
            Assert.NotEmpty(cut.FindAll(".mud-button-root"));
        });
    }

    private static LibraryItemViewModel CreateSampleLibraryItem() => new()
    {
        EntityId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Title = "The Hobbit",
        Author = "J.R.R. Tolkien",
        MediaType = "Books",
        Status = "Verified",
        RetailMatch = "google_books",
        WikidataMatch = "linked",
        WikidataQid = "Q15228",
        CreatedAt = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero),
        FileName = "the-hobbit.epub",
        FilePath = "C:\\Library\\Books\\The Hobbit.epub",
        FileSizeBytes = 1024 * 1024 * 5,
        IsReadyForLibrary = true,
        LibraryVisibility = "visible",
        Confidence = 0.92,
    };

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MediaEngine.Web.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    }
}
