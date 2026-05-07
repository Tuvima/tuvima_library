using MediaEngine.Contracts.Paging;

namespace MediaEngine.Api.Tests;

public sealed class PagingContractTests
{
    [Fact]
    public void PagedRequest_From_ClampsUnsafeInput()
    {
        var request = PagedRequest.From(offset: -10, limit: 10_000, defaultLimit: 100, maxLimit: 250);

        Assert.Equal(0, request.Offset);
        Assert.Equal(250, request.Limit);
    }

    [Fact]
    public void PagedResponse_FromPage_TrimsLimitPlusOneAndSetsCursor()
    {
        var request = new PagedRequest(50, 2);
        var response = PagedResponse<int>.FromPage([1, 2, 3], request);

        Assert.Equal([1, 2], response.Items);
        Assert.True(response.HasMore);
        Assert.Equal("52", response.NextCursor);
    }
}
