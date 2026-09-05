using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Tests;

public sealed class ClaimScopeRollupTests
{
    [Theory]
    [InlineData(MediaType.TV)]
    [InlineData(MediaType.Music)]
    public void WikidataIdentity_IsParentScopedForContainerMedia(MediaType mediaType)
    {
        Assert.Equal(ClaimScope.Parent, ClaimScopeCatalog.GetScope(BridgeIdKeys.WikidataQid, mediaType));
        Assert.Equal(ClaimScope.Parent, ClaimScopeCatalog.GetScope(MetadataFieldConstants.WikidataQidScope, mediaType));
        Assert.Equal(ClaimScope.Parent, ClaimScopeCatalog.GetScope(MetadataFieldConstants.QidResolutionMethod, mediaType));
    }
}
