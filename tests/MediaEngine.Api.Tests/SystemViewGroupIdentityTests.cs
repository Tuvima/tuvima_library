using MediaEngine.Api.Models;
using MediaEngine.Api.Services.ReadServices;

namespace MediaEngine.Api.Tests;

public sealed class SystemViewGroupIdentityTests
{
    [Fact]
    public void CreateId_IsStableAcrossEndpointAndDetailLookupFormatting()
    {
        var original = new ContentGroupDto
        {
            DisplayName = "Sprawl trilogy",
            Creator = "William Gibson",
            Year = "1984",
        };
        var reformatted = new ContentGroupDto
        {
            DisplayName = "  SPRAWL TRILOGY ",
            Creator = "william gibson",
            Year = "1984",
        };

        var endpointId = SystemViewGroupIdentity.CreateId(original, "Books", "series");
        var detailLookupId = SystemViewGroupIdentity.CreateId(reformatted, "books", "SERIES");

        Assert.Equal(endpointId, detailLookupId);
    }

    [Fact]
    public void CreateId_KeepsSameNamedGroupsFromDifferentCreatorsDistinct()
    {
        var first = new ContentGroupDto { DisplayName = "Legacy", Creator = "Author One" };
        var second = new ContentGroupDto { DisplayName = "Legacy", Creator = "Author Two" };

        Assert.NotEqual(
            SystemViewGroupIdentity.CreateId(first, "Books", "series"),
            SystemViewGroupIdentity.CreateId(second, "Books", "series"));
    }
}
