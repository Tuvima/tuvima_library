using Bunit;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class SharedUiPrimitiveTests : TestContext
{
    public SharedUiPrimitiveTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void AppMediaCard_RendersSquareVariantWithBadgeAndProgress()
    {
        var clicked = false;

        var cut = RenderComponent<AppMediaCard>(parameters => parameters
            .Add(component => component.Title, "Static on the Line")
            .Add(component => component.Subtitle, "Among The Outcasts")
            .Add(component => component.ImageUrl, "https://example.test/cover.jpg")
            .Add(component => component.Progress, 42)
            .Add(component => component.Variant, AppMediaCardVariant.Square)
            .Add(component => component.Badge, builder =>
            {
                builder.OpenComponent<AppStatusBadge>(0);
                builder.AddAttribute(1, nameof(AppStatusBadge.Text), "NEW");
                builder.AddAttribute(2, nameof(AppStatusBadge.Tone), AppStatusTone.Warning);
                builder.CloseComponent();
            })
            .Add(component => component.OnSelected, EventCallback.Factory.Create(this, () => clicked = true)));

        Assert.Single(cut.FindAll(".app-media-card--square"));
        Assert.Single(cut.FindAll(".app-media-artwork__image"));
        Assert.Contains("Static on the Line", cut.Markup);
        Assert.Contains("NEW", cut.Markup);

        cut.Find(".app-media-card").Click();
        Assert.True(clicked);
    }

    [Theory]
    [InlineData(AppPageStateKind.Loading, "Loading")]
    [InlineData(AppPageStateKind.Empty, "Nothing here")]
    [InlineData(AppPageStateKind.Error, "Could not load")]
    [InlineData(AppPageStateKind.Unavailable, "Unavailable")]
    public void AppPageState_RendersExpectedStateClass(AppPageStateKind kind, string title)
    {
        var cut = RenderComponent<AppPageState>(parameters => parameters
            .Add(component => component.Kind, kind)
            .Add(component => component.Title, title)
            .Add(component => component.Message, "State message"));

        Assert.Single(cut.FindAll($".app-page-state--{kind.ToString().ToLowerInvariant()}"));
        Assert.Contains(title, cut.Markup);
        Assert.Contains("State message", cut.Markup);
    }

    [Theory]
    [InlineData(AppStatusTone.Neutral, "app-status-badge--neutral")]
    [InlineData(AppStatusTone.Success, "app-status-badge--success")]
    [InlineData(AppStatusTone.Warning, "app-status-badge--warning")]
    [InlineData(AppStatusTone.Error, "app-status-badge--error")]
    public void AppStatusBadge_MapsToneToClass(AppStatusTone tone, string expectedClass)
    {
        var cut = RenderComponent<AppStatusBadge>(parameters => parameters
            .Add(component => component.Text, "Status")
            .Add(component => component.Tone, tone));

        Assert.Single(cut.FindAll($".{expectedClass}"));
        Assert.Contains("Status", cut.Markup);
    }

    [Fact]
    public void LegacyProviderTesterRoute_RedirectsIntoSettings()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();

        RenderComponent<ProviderTester>();

        Assert.EndsWith("/settings/provider-tester", navigation.Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyEnrichmentTesterRoute_RedirectsIntoSettings()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();

        RenderComponent<EnrichmentTester>();

        Assert.EndsWith("/settings/enrichment-tester", navigation.Uri, StringComparison.OrdinalIgnoreCase);
    }
}
