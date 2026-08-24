using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Profiles;

namespace MediaEngine.Api.Tests;

public sealed class ViewProfilePolicyContractTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AccessAndInclusionRemainIndependent(bool accessSharedView, bool includeInSharedView)
    {
        var profileId = Guid.NewGuid();
        var policy = ProfileContractMapper.ToDomain(profileId, new UpdateViewProfilePolicyRequest
        {
            ViewEnabled = true,
            AccessSharedView = accessSharedView,
            IncludeInSharedView = includeInSharedView,
            AllowGallerySharing = true,
        });

        var contract = ProfileContractMapper.ToResponse(policy);

        Assert.Equal(accessSharedView, contract.AccessSharedView);
        Assert.Equal(includeInSharedView, contract.IncludeInSharedView);
        Assert.True(contract.AllowGallerySharing);
    }
}
