using MediaEngine.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MediaEngine.Api.Tests;

public sealed class ViewImageRateLimitPartitionTests
{
    [Fact]
    public void ImageTrafficDoesNotConsumeControlBudgetAndCannotSelectPartitionByUrl()
    {
        var context = new DefaultHttpContext();
        var general = ViewImageRateLimitPartition.Key(context);
        context.Request.Path = "/view/items/anything/thumbnail";
        Assert.Equal(general, ViewImageRateLimitPartition.Key(context));
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointNameMetadata("GetViewItemThumbnail")), "thumbnail"));
        var thumbnail = ViewImageRateLimitPartition.Key(context);
        Assert.NotEqual(general, thumbnail);
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointNameMetadata("GetViewItemPreview")), "preview"));
        Assert.Equal(thumbnail, ViewImageRateLimitPartition.Key(context));
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointNameMetadata("GetViewItemContent")), "original"));
        Assert.Equal(general, ViewImageRateLimitPartition.Key(context));
    }
}
