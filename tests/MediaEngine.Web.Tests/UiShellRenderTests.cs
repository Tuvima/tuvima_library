using Bunit;
using MediaEngine.Web.Components.Collections;
using MediaEngine.Web.Components.Library;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Editing;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Services.Playback;
using MediaEngine.Web.Services.Theming;
using MediaEngine.Web.Shared;
using MediaEngine.Web.Tests.Support;
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
        Services.AddMudServices();
        Services.AddSingleton<IConfiguration>(configuration);
        Services.AddSingleton<IEngineApiClient>(apiClient);
        Services.AddSingleton<ThemeService>();
        Services.AddScoped<DeviceContextService>();
        Services.AddScoped<UniverseStateContainer>();
        Services.AddScoped<UIOrchestratorService>();
        Services.AddScoped<CollectionEditorLauncherService>();
        Services.AddScoped<ListenPlaybackService>();
    }

    [Fact]
    public void MainLayout_RendersMudShellAndBody()
    {
        var cut = RenderComponent<MainLayout>(parameters => parameters
            .Add(layout => layout.Body, builder => builder.AddMarkupContent(0, "<section id=\"test-body\">Body content</section>")));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".mud-appbar"));
            Assert.Equal(4, cut.FindAll(".layout-shell__nav-link").Count);
            Assert.Contains("Body content", cut.Markup);
            Assert.Contains("Library", cut.Markup);
            Assert.Contains("Search your library", cut.Markup);
        });
    }

    [Fact]
    public void SettingsPage_RendersMudTabsAndSystemContent()
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
            Assert.NotEmpty(cut.FindAll(".mud-tabs"));
            Assert.Contains("Runtime status", cut.Markup);
            Assert.Contains("Server identity and regional defaults", cut.Markup);
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
}
