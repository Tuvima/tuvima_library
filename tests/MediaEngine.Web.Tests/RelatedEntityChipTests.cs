using Bunit;
using MediaEngine.Contracts.Details;
using RelatedChipComponent = MediaEngine.Web.Components.Details.RelatedEntityChip;
using RelatedChipModel = MediaEngine.Contracts.Details.RelatedEntityChip;

namespace MediaEngine.Web.Tests;

public sealed class RelatedEntityChipTests : TestContext
{
    [Fact]
    public void RelatedEntityChip_RendersAnchorWhenRouteExists()
    {
        var chip = new RelatedChipModel
        {
            Id = "Q123",
            EntityType = RelatedEntityType.Universe,
            Label = "Sony Pictures Universe",
            Route = "/universe/Q123/explore",
        };

        var cut = RenderComponent<RelatedChipComponent>(parameters => parameters.Add(component => component.Chip, chip));

        var link = cut.Find("a.tl-related-chip");
        Assert.Equal("/universe/Q123/explore", link.GetAttribute("href"));
        Assert.Contains("Sony Pictures Universe", link.TextContent);
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void RelatedEntityChip_RendersStaticLabelWhenRouteIsMissing()
    {
        var chip = new RelatedChipModel
        {
            Id = "Sony Pictures",
            EntityType = RelatedEntityType.Organization,
            Label = "Sony Pictures",
        };

        var cut = RenderComponent<RelatedChipComponent>(parameters => parameters.Add(component => component.Chip, chip));

        var staticChip = cut.Find("span.tl-related-chip");
        Assert.Equal("true", staticChip.GetAttribute("aria-disabled"));
        Assert.Contains("Sony Pictures", staticChip.TextContent);
        Assert.Empty(cut.FindAll("a"));
        Assert.Empty(cut.FindAll("button"));
    }
}
