using System.Reflection;
using Bunit;
using MediaEngine.Contracts.Ingestion;
using MediaEngine.Web.Components.Setup;
using MediaEngine.Web.Services.Integration;
using MediaEngine.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class SetupAndLivePresentationTests : AsyncBunitContext
{
    public SetupAndLivePresentationTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AdministratorExplainsInvalidPasswordAndCanRevealIt()
    {
        var cut = Render<SetupAdministratorStage>(p => p
            .Add(x => x.Email, "owner@example.com")
            .Add(x => x.Password, "seven77")
            .Add(x => x.PasswordConfirmation, "seven77"));
        Assert.NotEmpty(cut.FindAll(".mud-input-error"));
        cut.Find("button[aria-label='Show password']").Click();
        var label = cut.FindAll("label").Single(x => x.TextContent == "Password");
        Assert.Equal("text", cut.Find("#" + label.GetAttribute("for")).GetAttribute("type"));
        cut.Render(p => p.Add(x => x.Password, "eight888").Add(x => x.PasswordConfirmation, "eight888"));
        Assert.Empty(cut.FindAll(".mud-input-error"));
        Assert.False(cut.FindAll("button").Single(x => x.TextContent.Contains("Create administrator")).HasAttribute("disabled"));
    }

    [Fact]
    public void TypingKeepsTheLivePasswordErrorUntilTheLengthIsValid()
    {
        var cut = Render<SetupAdministratorStage>();
        var label = cut.FindAll("label").Single(x => x.TextContent == "Password");
        var input = cut.Find("#" + label.GetAttribute("for"));
        input.Input("seven77");
        cut.WaitForAssertion(() => Assert.Equal("true", input.GetAttribute("aria-invalid")));
        input.Input("eight888");
        cut.WaitForAssertion(() => Assert.Equal("false", input.GetAttribute("aria-invalid")));
    }

    [Fact]
    public void UpdatedLivePreviewPreservesServerActivityOrderAndNewArtwork()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var batch = Guid.NewGuid();
        var before = new IngestionPresentationSnapshotDto { CurrentMedia = [new() { BatchId = batch, GroupId = first }, new() { BatchId = batch, GroupId = second }] };
        var after = new IngestionPresentationSnapshotDto { CurrentMedia = [new() { BatchId = batch, GroupId = second, CoverUrl = "/stream/artwork/new" }, new() { BatchId = batch, GroupId = first }] };
        var merge = typeof(IngestionLiveDashboardState).GetMethod("MergePresentation", BindingFlags.NonPublic | BindingFlags.Static)!;
        var actual = (IngestionPresentationSnapshotDto)merge.Invoke(null, [before, after])!;
        Assert.Equal(second, actual.CurrentMedia[0].GroupId);
        Assert.Equal("/stream/artwork/new", actual.CurrentMedia[0].CoverUrl);
    }
}
