using Bunit;
using MediaEngine.Web.Components.Pages;
using MediaEngine.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class SharedUiPrimitiveTests : TestContext
{
    public SharedUiPrimitiveTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
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
    public void AppButton_MapsToneSizeVariantAndClickCallback()
    {
        var clicked = false;

        var cut = RenderComponent<AppButton>(parameters => parameters
            .Add(component => component.Label, "Save")
            .Add(component => component.Tone, AppUiTone.Primary)
            .Add(component => component.Size, AppControlSize.Compact)
            .Add(component => component.ButtonStyle, AppButtonStyle.Filled)
            .Add(component => component.StartIcon, Icons.Material.Filled.Save)
            .Add(component => component.OnClick, EventCallback.Factory.Create(this, () => clicked = true)));

        Assert.Single(cut.FindAll(".app-button"));
        Assert.Single(cut.FindAll(".app-control--compact"));
        Assert.Single(cut.FindAll(".app-tone--primary"));
        Assert.Single(cut.FindAll(".app-button--filled"));
        Assert.Contains("Save", cut.Markup);

        cut.Find("button").Click();
        Assert.True(clicked);
    }

    [Fact]
    public void AppTextField_RendersLabelHelpTextAndSizeClass()
    {
        var cut = RenderComponent<AppTextField>(parameters => parameters
            .Add(component => component.Label, "Provider Name")
            .Add(component => component.Value, "TMDB")
            .Add(component => component.HelpText, "Shown below the field.")
            .Add(component => component.Size, AppControlSize.Large));

        Assert.Single(cut.FindAll(".app-field"));
        Assert.Single(cut.FindAll(".app-control--large"));
        Assert.Contains("Provider Name", cut.Markup);
        Assert.Contains("Shown below the field.", cut.Markup);
    }

    [Fact]
    public void AppSelect_RendersSharedFieldAndOptions()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<AppSelect>(1);
            builder.AddAttribute(2, nameof(AppSelect.Label), "Region");
            builder.AddAttribute(3, nameof(AppSelect.Value), "localized");
            builder.AddAttribute(4, nameof(AppSelect.Options), new[]
            {
                new AppSelectOption("source", "Source defaults"),
                new AppSelectOption("localized", "Localized metadata"),
            });
            builder.AddAttribute(5, nameof(AppSelect.Size), AppControlSize.Normal);
            builder.CloseComponent();
        });

        Assert.Single(cut.FindAll(".app-field"));
        Assert.Single(cut.FindAll(".app-control--normal"));
        Assert.Contains("Region", cut.Markup);
        Assert.Contains("Localized metadata", cut.Markup);
    }

    [Fact]
    public void AppSwitchRow_RendersLabelDescriptionAndDisabledState()
    {
        var cut = RenderComponent<AppSwitchRow>(parameters => parameters
            .Add(component => component.Label, "Status")
            .Add(component => component.Description, "Provider is enabled.")
            .Add(component => component.Value, true)
            .Add(component => component.Disabled, true));

        Assert.Single(cut.FindAll(".app-switch-row"));
        Assert.Contains("Status", cut.Markup);
        Assert.Contains("Provider is enabled.", cut.Markup);
        Assert.Contains("disabled", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppProviderLogo_UsesSharedSizingAndFallback()
    {
        var cut = RenderComponent<AppProviderLogo>(parameters => parameters
            .Add(component => component.FallbackText, "TM")
            .Add(component => component.AccentColor, "#22C55E")
            .Add(component => component.Size, AppControlSize.Large));

        Assert.Single(cut.FindAll(".app-provider-logo"));
        Assert.Single(cut.FindAll(".app-control--large"));
        Assert.Single(cut.FindAll(".app-provider-logo--fallback"));
        Assert.Contains("TM", cut.Markup);
    }

    [Fact]
    public void AppProviderLogo_UsesTransparentImageTreatmentForProviderAssets()
    {
        var cut = RenderComponent<AppProviderLogo>(parameters => parameters
            .Add(component => component.ImageUrl, "images/providers/tmdb.svg")
            .Add(component => component.AltText, "TMDB")
            .Add(component => component.Size, AppControlSize.Normal));

        Assert.Single(cut.FindAll(".app-provider-logo--image"));
        Assert.Single(cut.FindAll("img[src='images/providers/tmdb.svg']"));
        Assert.Empty(cut.FindAll(".app-provider-logo__fallback"));
    }

}
