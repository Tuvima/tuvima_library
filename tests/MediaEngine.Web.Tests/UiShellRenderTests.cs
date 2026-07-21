using Bunit;
using MediaEngine.Web.Components.Collections;
using MediaEngine.Web.Components.Library;
using MediaEngine.Web.Components.Listen;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Components.Settings;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.MediaTiles;
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
using System.Text.RegularExpressions;

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
        Services.AddScoped<ActiveProfileSessionService>();
        Services.AddScoped<UIOrchestratorService>();
        Services.AddScoped<CollectionEditorLauncherService>();
        Services.AddScoped<MediaTileComposerService>();
        Services.AddSingleton(new ListenPlaybackClientSettings());
        Services.AddScoped<PlaybackSessionController>();
        Services.AddScoped<ShellActivityState>();
        Services.AddSingleton(new DashboardAuthUiOptions(false));
        Services.AddScoped<ListenAudioDragService>();
        Services.AddScoped<IUserPlaybackPreferencesAccessor, UserPlaybackPreferencesAccessor>();
        Services.AddScoped<MediaReactionService>();
        Services.AddScoped<FavoriteService>();
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
            Assert.Equal(4, cut.FindAll(".layout-shell__nav-link").Count);
            Assert.Equal("/images/library.svg", cut.Find(".layout-shell__brand-lockup").GetAttribute("src"));
            Assert.Empty(cut.FindAll(".layout-shell__mobile-menu"));
            Assert.Contains("Body content", cut.Markup);
            Assert.DoesNotContain("Home", cut.Markup);
            Assert.DoesNotContain("Search your library", cut.Markup);
            Assert.Empty(cut.FindAll(".layout-shell__search-shell"));
            Assert.Single(cut.FindAll(".layout-shell__search-action"));
            Assert.Single(cut.FindAll(".top-nav-account-menu__trigger"));
            Assert.Single(cut.FindAll(".layout-shell__my-list"));
            Assert.Single(cut.FindAll(".system-activity-indicator"));
            Assert.Empty(cut.FindAll(".layout-shell__review-button"));
            Assert.Empty(cut.FindAll(".layout-shell__avatar-trigger"));
        });
    }

    [Fact]
    public void MainLayout_StartsLiveUpdatesAndUsesDedicatedNavbarComponents()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Shared", "MainLayout.razor"));
        var accountSource = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Navigation", "TopNavAccountMenu.razor"));
        var accountCss = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Navigation", "TopNavAccountMenu.razor.css"));
        var globalCss = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "wwwroot", "app.css"));
        var css = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Shared", "MainLayout.razor.css"));

        Assert.Contains("await Orchestrator.StartSignalRAsync();", source);
        Assert.Contains("await Activity.InitializeAsync();", source);
        Assert.Contains("<TopNavAccountMenu", source);
        Assert.Contains("<SystemActivityIndicator", source);
        Assert.Contains("/images/library.svg", source);
        Assert.DoesNotContain("<AppLogo", source);
        Assert.Contains("Nav_Search", source);
        Assert.Contains("OpenSearch", source);
        Assert.DoesNotContain("<MudTextField", source);
        Assert.Contains("TopBar_MyList", source);
        Assert.DoesNotContain("NotificationsNone", source);
        Assert.DoesNotContain("ToggleProfileMenu", source);
        Assert.Contains("@if (CanViewReview)", accountSource);
        Assert.Contains("TopBar_NeedsReview", accountSource);
        Assert.Contains("ShowSignOut", accountSource);
        Assert.DoesNotContain("NotificationsNone", accountSource);
        Assert.DoesNotContain("top-nav-account-menu__trigger-copy", accountSource);
        Assert.Contains("font-family: var(--font-brand);", css);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);", css);
        Assert.Contains("grid-column: 2;", css);
        Assert.Contains("font-family: var(--font-ui);", accountCss);
        Assert.Contains(".top-nav-account-menu__popover.mud-popover", globalCss);
    }

    [Fact]
    public void Orchestrator_RetriesLiveUpdatesWhenHealthyStatusFindsDisconnectedSignalR()
    {
        var source = File.ReadAllText(GetRepoFile(
            "src", "MediaEngine.Web", "Services", "Integration", "UIOrchestratorService.cs"));

        Assert.Contains("status.IsHealthy && !IsIntercomConnected", source);
        Assert.Contains("await StartSignalRAsync(ct);", source);
        Assert.Contains("HubConnectionState.Disconnected", source);
        Assert.Contains("A later call can retry", source);
    }

    [Fact]
    public void SettingsPage_RendersSharedSidebarAndAdminOverviewContent()
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
            builder.AddAttribute(4, nameof(Settings.Section), "admin");
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".sidebar-page"));
            Assert.Single(cut.FindAll(".sidebar-rail"));
            Assert.Empty(cut.FindAll(".mud-tabs"));
            Assert.Contains("Ingestion Progress", cut.Markup);
            Assert.Contains("Review Queue", cut.Markup);
            Assert.Contains("No ingestion currently running", cut.Markup);
            Assert.Contains("View run", cut.Markup);
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
            Assert.Contains("Change profile photo", cut.Markup);
            Assert.Contains("Continue Your Activity", cut.Markup);
            Assert.Contains("Recent History", cut.Markup);
            Assert.Contains("Your Taste / Top Genres", cut.Markup);
            Assert.DoesNotContain("At a Glance", cut.Markup);
            Assert.DoesNotContain("Your Statistics", cut.Markup);
            Assert.DoesNotContain("Recently Added", cut.Markup);
            Assert.DoesNotContain("Recently Completed", cut.Markup);
            Assert.DoesNotContain("Libraries Used", cut.Markup);
            Assert.DoesNotContain("Preferences at a Glance", cut.Markup);
            Assert.DoesNotContain("user-overview-name-field", cut.Markup);
            Assert.DoesNotContain("user-overview-editor", cut.Markup);
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
            Assert.Contains("Review Metadata", cut.Markup);
        });
    }

    [Fact]
    public void PrivacyHistoryTab_DisablesUnavailableControlsAndExplainsThatNoDataChanges()
    {
        var cut = RenderComponent<PrivacyHistoryTab>();

        Assert.Contains("do not change, delete, export, or save any data", cut.Markup);
        Assert.NotEmpty(cut.FindAll("button"));
        Assert.All(cut.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.All(cut.FindAll("input"), input => Assert.True(input.HasAttribute("disabled")));

        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Settings", "PrivacyHistoryTab.razor"));
        Assert.DoesNotContain("OnClick=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackSettingsPage_RendersTabbedUserPlaybackPreferences()
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
            builder.AddAttribute(4, nameof(Settings.Section), "playback");
            builder.CloseComponent();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll(".mud-tabs"));
            Assert.NotEmpty(cut.FindAll(".settings-tab-strip"));
            Assert.NotEmpty(cut.FindAll(".settings-summary-strip"));
            Assert.NotEmpty(cut.FindAll(".settings-section-card--dense"));
            Assert.NotEmpty(cut.FindAll(".settings-preference-row"));
            Assert.Contains("General", cut.Markup);
            Assert.Contains("Watching", cut.Markup);
            Assert.Contains("Listening", cut.Markup);
            Assert.Contains("Reading", cut.Markup);
            Assert.Contains("Subtitles", cut.Markup);
            Assert.DoesNotContain("Saved locally", cut.Markup);
            Assert.DoesNotContain("Save changes", cut.Markup);
            Assert.DoesNotContain("Unsaved changes", cut.Markup);
            Assert.DoesNotContain("Web delivery", cut.Markup);
            Assert.DoesNotContain("TV delivery", cut.Markup);
            Assert.DoesNotContain("Mobile download profile", cut.Markup);
            Assert.DoesNotContain("Direct play", cut.Markup);
            Assert.DoesNotContain("HLS", cut.Markup);
            Assert.DoesNotContain("Transcoding", cut.Markup);
        });
    }

    [Fact]
    public void PlaybackTab_UsesMudSlidersAndRealApiSavePath()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Settings", "PlaybackTab.razor"));

        Assert.Contains("<AppTabs", source);
        Assert.Contains("settings-tab-strip", source);
        Assert.Contains("settings-summary-strip", source);
        Assert.Contains("settings-field--compact", source);
        Assert.Contains("settings-preference-row", source);
        Assert.Contains("settings-slider-block", source);
        Assert.Contains("UpdateAndSaveAsync", source);
        Assert.Contains("Video Speed", source);
        Assert.Contains("Audiobook Speed", source);
        Assert.Contains("Audiobook Chapters", source);
        Assert.Contains("DetectShortIntroChapters", source);
        Assert.Contains("ShortIntroMaxSeconds", source);
        Assert.Contains("ShortIntroLabel", source);
        Assert.Contains("NormalizeIntroLabel", source);
        Assert.Contains("<MudSlider T=\"double\"", source);
        Assert.Contains("<MudSlider T=\"int\"", source);
        Assert.Contains("<AppTextField T=\"string\"", source);
        Assert.Contains("Orchestrator.SavePlaybackSettingsAsync", source);
        Assert.DoesNotContain("Saved locally", source);
        Assert.DoesNotContain("Save changes", source);
        Assert.DoesNotContain("Unsaved changes", source);
        Assert.DoesNotContain("Task.Delay", source);
        Assert.DoesNotContain("MudToggleGroup", source);
        Assert.DoesNotContain("Web delivery", source);
        Assert.DoesNotContain("TV delivery", source);
        Assert.DoesNotContain("Mobile download profile", source);
    }

    [Fact]
    public void DeliverySettings_MarksUnpersistedControlsAsNotConnected()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Settings", "PlaybackDeliverySettingsTab.razor"));

        Assert.Contains("Not connected. Direct Play settings are planned", source);
        Assert.Contains("Not connected. Subtitle and audio delivery settings are planned", source);
        Assert.Matches(@"<AppSwitchRow[^>]*Label=""Allow direct play""[^>]*Disabled=""true""", source);
        Assert.Matches(@"<AppSwitchRow[^>]*Label=""Subtitle extraction""[^>]*Disabled=""true""", source);
        Assert.DoesNotContain("TODO: Persist direct play settings", source);
        Assert.DoesNotContain("TODO: Persist subtitle and audio delivery settings", source);
    }

    [Fact]
    public void AiFeatures_UsesEngineBackedConfigSavePath()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Settings", "AiFeaturesTab.razor"));

        Assert.Contains("Save feature flags", source);
        Assert.Contains("SaveAiConfigAsync", source);
        Assert.Contains("GetAiConfigAsync", source);
        Assert.Contains("GetAiModelStatusesAsync", source);
        Assert.Contains("Not connected", source);
        Assert.DoesNotContain("Toggle persistence is not wired to the Engine yet", source);
    }

    [Fact]
    public void ModelsTab_UsesEngineBackedLifecycleActions()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Settings", "ModelsTab.razor"));

        Assert.Contains("GetAiModelStatusesAsync", source);
        Assert.Contains("StartAiModelDownloadAsync", source);
        Assert.Contains("CancelAiModelDownloadAsync", source);
        Assert.Contains("LoadAiModelAsync", source);
        Assert.Contains("UnloadAiModelAsync", source);
        Assert.Contains("result.Problem?.ToUserMessage()", source);
        Assert.Contains("RunAiModelBenchmarkAsync", source);
        Assert.DoesNotContain("SimulateDownload", source);
        Assert.DoesNotContain("Task.Delay", source);
        Assert.DoesNotContain("ChangeStatus", source);
    }

    [Fact]
    public void LocalAiLimits_AreMarkedNotConnectedAndModelsAreNotDuplicated()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Settings", "LocalAiSettingsTab.razor"));

        Assert.Contains("GetAiConfigAsync", source);
        Assert.Contains("SaveAiConfigAsync", source);
        Assert.Contains("Local AI runs on this server", source);
        Assert.Single(Regex.Matches(source, "<ModelsTab />"));
        Assert.DoesNotContain("TODO: Persist AI limits", source);
    }

    [Fact]
    public void ProviderPriority_DoesNotFallBackToHardcodedDefaultsAsConfiguredState()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Settings", "ProviderPriorityTab.razor"))
                     + File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Settings", "ProviderPriorityTab.razor.cs"));

        Assert.Contains("sample data is not presented as live configuration", source);
        Assert.Contains("_loadError", source);
        Assert.Contains("InitializeEmptyAssignments", source);
        Assert.Contains("sample data is not presented as live configuration", source);
        Assert.DoesNotContain("Load sample chain", source);
        Assert.DoesNotContain("using dashboard defaults", source);
        Assert.DoesNotContain("ResetToDefaults", source);
    }

    [Fact]
    public void SettingsNavigation_LabelsPartialAndPlannedAdminSections()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Pages", "Settings.razor"));

        var nav = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Models", "ViewDTOs", "SettingsNav.cs"));

        Assert.Contains("SettingsSection.LocalAi", nav);
        Assert.Contains("SettingsSection.Delivery", nav);
        Assert.Contains("SettingsSection.Plugins", nav);
        Assert.Contains("SettingsSection.Access", nav);
        Assert.Contains("SettingsStatusKind.Partial", nav);
    }

    [Fact]
    public void Source_ExposesCurrentDashboardRoutes()
    {
        var routeSources = Directory
            .EnumerateFiles(GetRepoFile("src", "MediaEngine.Web", "Components", "Pages"), "*.razor", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Contains(routeSources, source => source.Contains("@page \"/\"", StringComparison.Ordinal));
        Assert.Contains(routeSources, source => source.Contains("@page \"/read", StringComparison.Ordinal));
        Assert.Contains(routeSources, source => source.Contains("@page \"/watch", StringComparison.Ordinal));
        Assert.Contains(routeSources, source => source.Contains("@page \"/listen", StringComparison.Ordinal));
        Assert.Contains(routeSources, source => source.Contains("@page \"/collections", StringComparison.Ordinal));
        Assert.Contains(routeSources, source => source.Contains("@page \"/search", StringComparison.Ordinal));
        Assert.Contains(routeSources, source => source.Contains("@page \"/settings", StringComparison.Ordinal));
    }

    [Fact]
    public void CollectionsPage_RendersCentralizedBrowseShell()
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
            Assert.Empty(cut.FindAll(".mud-table"));
            Assert.NotEmpty(cut.FindAll(".browse-shell"));
            Assert.Empty(cut.FindAll(".cinematic-hero-carousel"));
            Assert.NotEmpty(cut.FindAll(".surface-tab-bar"));
            Assert.NotEmpty(cut.FindAll(".browse-shell__search"));
            Assert.NotEmpty(cut.FindAll(".browse-shell__sort"));
            Assert.NotEmpty(cut.FindAll(".browse-shell__grid"));
            Assert.NotEmpty(cut.FindAll(".media-group-tile"));
            Assert.Empty(cut.FindAll(".media-tile"));
            Assert.Empty(cut.FindAll(".collections-hub__tabs"));
            Assert.Empty(cut.FindAll(".collections-hub-tab"));
            Assert.Empty(cut.FindAll(".collection-hub-section"));
            Assert.Empty(cut.FindAll(".collection-hub-card"));
            Assert.Empty(cut.FindAll(".collection-hub-row"));
            Assert.Empty(cut.FindAll(".collection-inspector"));
            Assert.Contains("Dune Universe", cut.Markup);
            Assert.Contains("Middle-earth", cut.Markup);
            Assert.Contains("Weekend Picks", cut.Markup);
            Assert.Contains(">Playlists<", cut.Markup);
            Assert.Contains("Broader rollups", cut.Markup);
            Assert.Contains("Search collections", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".app-select"));
            Assert.DoesNotContain("CROSS-MEDIA COLLECTIONS", cut.Markup);
            Assert.DoesNotContain("WATCH COLLECTIONS", cut.Markup);
            Assert.DoesNotContain("LISTEN COLLECTIONS", cut.Markup);
            Assert.DoesNotContain("READ COLLECTIONS", cut.Markup);
            Assert.DoesNotContain("collections-hub__browse-types", cut.Markup);
        });
    }

    [Fact]
    public void LibraryConfigurableTable_RendersMudTableShellAndMudActions()
    {
        var cut = RenderComponent<LibraryConfigurableTable>(parameters => parameters
            .Add(component => component.Columns, LibraryColumnDefinitions.GetColumnsByTab("books"))
            .Add(component => component.Items, [CreateSampleLibraryItem()])
            .Add(component => component.Loading, false)
            .Add(component => component.GroupBy, "type")
            .Add(component => component.SelectedItems, new HashSet<Guid>()));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".mud-simple-table"));
            Assert.Contains("The Hobbit", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".mud-button-root"));
            var groupToggle = cut.Find("button.vmt-group-toggle");
            Assert.Equal("true", groupToggle.GetAttribute("aria-expanded"));
        });
    }

    [Fact]
    public void ListenNavigationSection_RendersCurrentRouteAsSemanticLink()
    {
        var cut = RenderComponent<ListenNavigationSection>(parameters => parameters
            .Add(component => component.Label, "Library")
            .Add(component => component.Items,
            [
                new ListenNavigationItem("Home", "/listen", Icons.Material.Outlined.Home),
                new ListenNavigationItem("Music", "/listen/music", Icons.Material.Outlined.MusicNote),
            ])
            .Add(component => component.IsRouteActive, route => route == "/listen/music"));

        var links = cut.FindAll("a.listen-rail__item");
        Assert.Collection(
            links,
            link => Assert.Null(link.GetAttribute("aria-current")),
            link => Assert.Equal("page", link.GetAttribute("aria-current")));
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
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/listen/music/songs");

        var cut = RenderComponent<ListenPage>(parameters => parameters
            .Add(page => page.Section, "songs"));

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".listen-page"));
            Assert.Single(cut.FindAll(".listen-rail-shell"));
            Assert.Single(cut.FindAll(".listen-rail"));
            Assert.Single(cut.FindAll(".listen-content"));
            Assert.Single(cut.FindAll(".listen-now-panel"));
            Assert.Empty(cut.FindAll(".listen-topbar__menu"));
            Assert.Empty(cut.FindAll(".listen-rail__close"));
            Assert.DoesNotContain("Pins", cut.Markup);
        });
    }

    [Fact]
    public void ListenPage_MainHubRendersInsidePermanentListenShell()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/listen");

        var cut = RenderListenPageWithProviders();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".media-hub--listen"));
            Assert.Single(cut.FindAll("[data-media-hub='true']"));
            Assert.Empty(cut.FindAll(".media-lane-header__identity"));
            Assert.Contains(">Discover<", cut.Markup);
            Assert.Contains(">Music<", cut.Markup);
            Assert.Contains(">Audiobooks<", cut.Markup);
            Assert.Single(cut.FindAll(".media-hub__shelves"));
            Assert.Single(cut.FindAll(".listen-rail-shell"));
            Assert.Single(cut.FindAll(".listen-now-panel"));
            Assert.Single(cut.FindAll(".listen-page"));
        });
    }

    [Fact]
    public void ListenPage_RendersMusicQuickAccessAndPlaylistsInOrder()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/listen/music/songs");

        var cut = RenderComponent<ListenPage>(parameters => parameters
            .Add(page => page.Section, "songs"));

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            var library = markup.IndexOf(">Library<", StringComparison.Ordinal);
            var formats = markup.IndexOf(">Formats<", StringComparison.Ordinal);
            var yourLibrary = markup.IndexOf(">Your Library<", StringComparison.Ordinal);
            var playlists = markup.IndexOf(">Playlists<", StringComparison.Ordinal);

            Assert.True(library >= 0, "Library section should render.");
            Assert.True(formats > library, "Formats should render below Library.");
            Assert.True(yourLibrary > formats, "Your Library should render below Formats.");
            Assert.True(playlists > yourLibrary, "Playlists should render below Your Library.");
            Assert.DoesNotContain(">All Audio<", markup);
            Assert.Contains(">Audiobooks<", markup);
            Assert.Contains(">Music<", markup);
            Assert.Contains(">Songs<", markup);
            Assert.Single(Regex.Matches(markup, ">Recently Added<"));
            Assert.DoesNotContain(">Genres<", markup);
            Assert.Single(cut.FindAll(".listen-rail__section-toggle"));
            Assert.Contains("Summer Movies", markup);
            Assert.Contains("Add playlist", markup);
            Assert.Contains("Drag to reorder Summer Movies", markup);
            Assert.DoesNotContain(">Podcasts<", markup);
            Assert.DoesNotContain(">Radio<", markup);
            Assert.DoesNotContain("Edit playlist", markup);
            Assert.DoesNotContain("New Playlist Folder", markup);
            Assert.DoesNotContain("listen-create-modal", markup);
        });
    }

    [Fact]
    public void ListenPage_AudiobooksUsesDedicatedDashboardWithoutBrowseShellChrome()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/listen/audiobooks");

        var cut = RenderListenPageWithProviders();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".listen-page"));
            Assert.Single(cut.FindAll(".listen-page--audiobooks"));
            Assert.Single(cut.FindAll(".audiobooks-dashboard"));
            Assert.Empty(cut.FindAll(".browse-shell--embedded"));
            Assert.Contains("Search audiobooks", cut.Markup);
            Assert.Contains("All Audiobooks", cut.Markup);
            Assert.Contains("Series", cut.Markup);
            Assert.Contains("Authors", cut.Markup);
            Assert.Contains("In Progress", cut.Markup);
            Assert.Contains("Unread", cut.Markup);
            Assert.Contains("Length", cut.Markup);
            Assert.Contains("Recently Added", cut.Markup);
            Assert.Contains("Title", cut.Markup);
            Assert.Contains("Author", cut.Markup);
            Assert.Contains("Cards", cut.Markup);
            Assert.Contains("List", cut.Markup);
            Assert.Contains("Continue Listening", cut.Markup);
            Assert.Contains("Browse All", cut.Markup);
            Assert.Contains("Featured Series", cut.Markup);
            Assert.Contains("Dune Audiobook", cut.Markup);
            Assert.Contains("Frank Herbert", cut.Markup);
            Assert.DoesNotContain("Test Track", cut.Markup);
        });
    }

    [Fact]
    public void ListenPage_AudiobookSeriesTabChangesDashboardView()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/listen/audiobooks");

        var cut = RenderListenPageWithProviders();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".audiobooks-dashboard"));
            Assert.Contains("Series", cut.Markup);
        });

        var seriesButton = cut.FindAll(".audiobooks-tab")
            .Single(button => button.TextContent.Contains("Series", StringComparison.OrdinalIgnoreCase));
        seriesButton.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.EndsWith("/listen/audiobooks", navigationManager.Uri, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">Series<", cut.Markup);
            Assert.Contains("No audiobook series match these filters.", cut.Markup);
        });
    }

    [Fact]
    public void ListenPage_LeftRailAudiobooksUsesRealLinkForFirstClickNavigation()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/listen/music");

        var cut = RenderListenPageWithProviders();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll(".listen-page"));
            Assert.Contains("Audiobooks", cut.Markup);
        });

        var audiobooksLink = cut.FindAll("a.listen-rail__item[href='/listen/audiobooks']")
            .Single(link => link.TextContent.Contains("Audiobooks", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("/listen/audiobooks", audiobooksLink.GetAttribute("href"));
        Assert.Equal("_top", audiobooksLink.GetAttribute("target"));
        Assert.Equal("false", audiobooksLink.GetAttribute("data-enhance-nav"));
        Assert.Empty(cut.FindAll("button.listen-rail__item"));
    }

    [Fact]
    public void ListenPage_RouteParametersReloadListenStateWithRaceGuard()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Pages", "ListenPage.razor.cs"));
        var markup = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Pages", "ListenPage.razor"));
        var css = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Pages", "ListenPage.razor.css"));

        Assert.Contains("protected override async Task OnParametersSetAsync()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocationChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OnListenLocationChanged", source, StringComparison.Ordinal);
        Assert.Contains("private int _listenLoadVersion;", source, StringComparison.Ordinal);
        Assert.Contains("if (!IsCurrentLoad(loadVersion))", source, StringComparison.Ordinal);
        Assert.Contains("private void NavigateRailLink(string route)", source, StringComparison.Ordinal);
        Assert.Contains("NavigateTo(route, forceLoad: true)", source, StringComparison.Ordinal);
        Assert.Contains("target=\"_top\"", markup, StringComparison.Ordinal);
        Assert.Contains("NavigateRailLink(item.Route)", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Icons.Material.Outlined.DownloadDone", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Icons.Material.Outlined.RadioButtonUnchecked", markup, StringComparison.Ordinal);
        Assert.DoesNotContain(".listen-page--audiobooks .listen-rail", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".listen-page--audiobooks ::deep .listen-now-panel", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".listen-page--audiobooks {\r\n    grid-template-columns", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".listen-page--audiobooks {\n    grid-template-columns", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ListenDesktopPlayer_UsesPersistentRightPanelAndHidesBottomHostOnListenRoutes()
    {
        var panelSource = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Listen", "ListenNowPlayingPanel.razor"));
        var panelCss = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Listen", "ListenNowPlayingPanel.razor.css"));
        var hostSource = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Listen", "ListenNowPlayingBar.razor"));
        var hostCss = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Listen", "ListenNowPlayingBar.razor.css"));

        Assert.Contains("listen-now-panel", panelSource, StringComparison.Ordinal);
        Assert.Contains("Now Playing", panelSource, StringComparison.Ordinal);
        Assert.Contains("Up Next", panelSource, StringComparison.Ordinal);
        Assert.Contains("Drag tracks here", panelSource, StringComparison.Ordinal);
        Assert.Contains("TransportCommandRequested", hostSource, StringComparison.Ordinal);
        Assert.Contains("ReportHeartbeatAsync", hostSource, StringComparison.Ordinal);
        Assert.Contains("listen-player-shell--listen-route", hostSource, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 1181px)", hostCss, StringComparison.Ordinal);
        Assert.Contains(".listen-player-shell--listen-route", hostCss, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1180px)", panelCss, StringComparison.Ordinal);
        Assert.Contains("display: none;", panelCss, StringComparison.Ordinal);
    }

    [Fact]
    public void CinematicHero_RendersScopedRootSoListenHomeSidebarStaysVisible()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Cinematic", "CinematicHeroCarousel.razor"));

        Assert.Contains("<section class=\"@CarouselClass\"", source, StringComparison.Ordinal);
        Assert.Contains("<CinematicHeroSurface", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ListenPage_MusicEntryUsesDirectTiledAlbumBrowse()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/listen/music");

        var cut = RenderListenPageWithProviders();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Search albums or artists", cut.Markup);
            Assert.Empty(cut.FindAll(".listen-home"));
            Assert.Empty(cut.FindAll(".media-tile-shelf-scroll"));
            Assert.DoesNotContain("listen-mode-switch", cut.Markup);
        });

        var markup = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Pages", "ListenPage.razor"));
        Assert.Contains("<MediaTileGrid Items=\"@AlbumTiles\"", markup, StringComparison.Ordinal);
        Assert.Contains("Class=\"listen-card-grid listen-card-grid--albums\"", markup, StringComparison.Ordinal);
        Assert.Contains("ShowCompactCaptions=\"true\"", markup, StringComparison.Ordinal);
        Assert.Contains("TileSizePx=\"@_albumTileSizePx\"", markup, StringComparison.Ordinal);
        Assert.Contains("AriaLabel=\"Album tile size\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAllAudiobooks", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionEditor_PlaylistMode_UsesSharedDialogArtworkPickerAndCompactFields()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Collections", "CollectionEditorShell.razor"));

        Assert.Contains("<AppDialogShell", source);
        Assert.Contains("IsPlaylistLaunch", source);
        Assert.Contains("app-artwork-picker", source);
        Assert.Contains("app-artwork-picker__edit", source);
        Assert.Contains("app-dialog-field", source);
        Assert.DoesNotContain("Choose file", source);
        Assert.DoesNotContain("listen-create-modal", source);
    }

    [Fact]
    public void CollectionEditor_SmartPlaylistMode_RendersSimplifiedMusicRuleControls()
    {
        var source = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "Components", "Collections", "CollectionEditorShell.razor"));

        Assert.Contains("SmartPlaylist", source);
        Assert.Contains("Smart Playlist", source);
        Assert.DoesNotContain("app-icon-picker__item", source);
        Assert.DoesNotContain("Show in My Profile and Search", source);
        Assert.DoesNotContain("<MudSelectItem T=\"string\" Value=\"@(\"media_type\")\">Media Type</MudSelectItem>", source);
        Assert.Contains("Add rule", source);
        Assert.Contains("collection-editor-rule-row", source);
        Assert.Contains("app-dialog-select", source);
    }

    [Fact]
    public void SharedDialogCss_DefinesCompactFieldsDarkSelectsAndHiddenArtworkInput()
    {
        var css = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "wwwroot", "app.css"));

        Assert.Contains(".app-dialog-field", css);
        Assert.Contains("var(--tl-control-height-sm)", css);
        Assert.Contains(".tl-form", css);
        Assert.Contains(".tl-field-label", css);
        Assert.Contains(".tl-filter-bar", css);
        Assert.Contains(".tl-action-bar", css);
        Assert.Contains(".mud-popover .mud-paper", css);
        Assert.Contains(".app-artwork-picker__input", css);
        Assert.Contains("opacity: 0", css);
    }

    [Fact]
    public void AppCss_KeepsMudDropdownRowsBorderless()
    {
        var css = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "wwwroot", "app.css"));

        Assert.Contains(".mud-menu .mud-list-item", css);
        Assert.Contains("border: 0 !important;", css);
        Assert.Contains("box-shadow: none !important;", css);

        var rowRuleBodies = Regex.Matches(css, @"(?s)\.mud-(?:list-item|menu-item)[^{]*\{(?<body>.*?)\}")
            .Select(match => match.Groups["body"].Value.ToLowerInvariant())
            .ToArray();

        Assert.All(rowRuleBodies, body =>
        {
            Assert.DoesNotContain("border: 1px", body);
            Assert.DoesNotContain("box-shadow: var(", body);
            Assert.DoesNotContain("box-shadow: 0 ", body);
            Assert.DoesNotContain("box-shadow: inset", body);
        });
    }

    [Fact]
    public void DesignTokens_DefineSharedControlAndFormSizing()
    {
        var tokens = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "wwwroot", "tuvima.tokens.css"));

        Assert.Contains("--tl-control-height-sm: 36px;", tokens);
        Assert.Contains("--tl-control-height-md: 42px;", tokens);
        Assert.Contains("--tl-control-height-lg: 48px;", tokens);
        Assert.Contains("--tl-control-radius: 10px;", tokens);
        Assert.Contains("--tl-form-row-gap: 16px;", tokens);
        Assert.Contains("--tl-form-section-gap: 24px;", tokens);
        Assert.Contains("--tl-card-padding: 20px;", tokens);
        Assert.Contains("--tl-text-faint:", tokens);
        Assert.Contains("--tl-font-mono:", tokens);
    }

    [Fact]
    public void AppCss_UsesSingleAliasRootAndSharedFoundation()
    {
        var css = File.ReadAllText(GetRepoFile("src", "MediaEngine.Web", "wwwroot", "app.css"));

        Assert.Single(Regex.Matches(css, @"(?m)^:root\s*\{"));
        Assert.Contains(".tl-setting-row", css);
        Assert.Contains(".tl-card--flush", css);
        Assert.Contains(".tl-empty-state", css);
        Assert.Contains(".search-result-row", css);
        Assert.Contains("border-right: 1px solid var(--tl-divider)", css);
        Assert.DoesNotContain("Legacy settings shared UI components", css);
    }

    [Fact]
    public void WebUiSource_DoesNotContainLegacySettingsClassesOrMojibake()
    {
        var root = GetRepoFile("src", "MediaEngine.Web");
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.DoesNotMatch(@"class=""[^""]*\bst-page\b", source);
            Assert.DoesNotMatch(@"class=""[^""]*\bst-card\b", source);
            Assert.DoesNotMatch(@"class=""[^""]*\bst-toggle-row\b", source);
            Assert.DoesNotContain("Ã‚", source);
            Assert.DoesNotContain("Ã¢", source);
            Assert.DoesNotContain("Ã", source);
            Assert.DoesNotContain("Â", source);
            Assert.DoesNotContain("â€¦", source);
            Assert.DoesNotContain("â€”", source);
            Assert.DoesNotContain("â€“", source);
            Assert.DoesNotContain("â†", source);
            Assert.DoesNotContain("â€", source);
            Assert.DoesNotContain("â”", source);
            Assert.DoesNotContain("ï¿½", source);
            Assert.DoesNotContain("�", source);
        }
    }

    [Fact]
    public void UiConsistencyAudit_DocumentsMigrationPlan()
    {
        var audit = File.ReadAllText(GetRepoFile("docs", "ui", "ui-consistency-audit.md"));

        Assert.Contains("Inconsistent Form And Input Styling", audit);
        Assert.Contains("Inline Styles", audit);
        Assert.Contains("Duplicate Card And Panel Patterns", audit);
        Assert.Contains("Recommended Priority Order", audit);
        Assert.Contains("tl-filter-bar", audit);
    }

    private static string GetRepoFile(params string[] segments) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", ".." }.Concat(segments).ToArray()));

    private IRenderedFragment RenderListenPageWithProviders() => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<MudDialogProvider>(1);
        builder.CloseComponent();
        builder.OpenComponent<MudSnackbarProvider>(2);
        builder.CloseComponent();
        builder.OpenComponent<ListenPage>(3);
        builder.CloseComponent();
    });

    [Fact]
    public void SearchPage_RendersMudSearchResultsWithoutRawCards()
    {
        Services.AddSingleton<IEngineApiClient>(EngineApiClientStub.Create(stub =>
        {
            stub.SetHandler(nameof(IEngineApiClient.SearchWorksAsync), _ => Task.FromResult(new List<SearchResultViewModel>
            {
                new()
                {
                    WorkId = Guid.Parse("30000000-0000-0000-0000-000000000101"),
                    CollectionId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                    Title = "Dune",
                    Author = "Frank Herbert",
                    MediaType = "Book",
                    CollectionDisplayName = "Dune",
                },
            }));
        }));

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameter("q", "dune"));

        var cut = RenderComponent<SearchPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Dune", cut.Markup);
            Assert.Contains("Frank Herbert", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".search-results-list"));
            Assert.NotEmpty(cut.FindAll(".search-result-row"));
            Assert.NotEmpty(cut.FindAll(".mud-link"));
        });
    }

    private static LibraryItemViewModel CreateSampleLibraryItem() => new()
    {
        EntityId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Title = "The Hobbit",
        Author = "J.R.R. Tolkien",
        MediaType = "Books",
        Status = "Verified",
        RetailMatch = "open_library",
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
