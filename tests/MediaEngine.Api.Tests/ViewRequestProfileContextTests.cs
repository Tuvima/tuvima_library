using MediaEngine.Api.Services.View;
using Microsoft.AspNetCore.Http;

namespace MediaEngine.Api.Tests;

public sealed class ViewRequestProfileContextTests
{
    [Fact]
    public void RawBrowserProfileIdentifiersAreNeverTrusted()
    {
        var requested = Guid.NewGuid();
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString($"?profileId={requested}");
        http.Request.Headers["X-Tuvima-Profile"] = requested.ToString();
        var accessor = new HttpContextAccessor { HttpContext = http };
        var context = new HttpViewRequestProfileContext(accessor);

        Assert.Null(context.Current);

        var trusted = new ViewRequestProfile(Guid.NewGuid(), "RestrictedProfile");
        HttpViewRequestProfileContext.SetTrustedProfile(http, trusted);
        Assert.Equal(trusted, context.Current);
    }
}
