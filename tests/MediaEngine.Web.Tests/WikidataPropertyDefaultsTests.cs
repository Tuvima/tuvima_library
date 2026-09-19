using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Tests;

public sealed class WikidataPropertyDefaultsTests
{
    [Fact]
    public void UniverseCatalog_UsesExactP171AndP169Semantics()
    {
        var properties = WikidataPropertyDefaults.GetAllProperties();

        Assert.DoesNotContain(properties, property => property.PCode == "P171");
        Assert.Contains(properties, property => property.PCode == "P169"
            && property.ClaimKey == "chief_executive_officer"
            && property.Category == "Universe: Organization");
    }

    [Fact]
    public void UniverseCatalog_IncludesEventAndObjectContextualProperties()
    {
        var properties = WikidataPropertyDefaults.GetAllProperties();

        Assert.Contains(properties, property => property.PCode == "P710"
            && property.Category == "Universe: Event");
        Assert.Contains(properties, property => property.PCode == "P828"
            && property.Category == "Universe: Event");
        Assert.Contains(properties, property => property.PCode == "P1542"
            && property.Category == "Universe: Event");
        Assert.Contains(properties, property => property.PCode == "P4584"
            && property.Category == "Universe: Object");
        Assert.Contains(properties, property => property.PCode == "P5800"
            && property.Category == "Universe: Object");
        Assert.Contains(properties, property => property.PCode == "P793"
            && property.ClaimKey == "narrative_event"
            && property.Category == "Stage 1: Story & Narrative");
        Assert.Equal(17, WikidataPropertyDefaults.CategoryOrder("Universe: Event"));
        Assert.Equal(18, WikidataPropertyDefaults.CategoryOrder("Universe: Object"));
    }
}
