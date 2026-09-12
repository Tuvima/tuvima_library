using System.Net.Sockets;
using MediaEngine.Contracts.Startup;

namespace MediaEngine.Api.Tests;

public sealed class ProcessInstanceLeaseTests
{
    [Fact]
    public void Lease_AllowsOnlyOneConcurrentOwner_AndCanBeReacquired()
    {
        var leaseName = $"TuvimaLibrary.Tests.{Guid.NewGuid():N}";

        using (var first = ProcessInstanceLease.TryAcquire(leaseName))
        using (var second = ProcessInstanceLease.TryAcquire(leaseName))
        {
            Assert.True(first.IsAcquired);
            Assert.False(second.IsAcquired);
        }

        using var replacement = ProcessInstanceLease.TryAcquire(leaseName);
        Assert.True(replacement.IsAcquired);
    }

    [Fact]
    public void AddressClassifier_FindsNestedSocketAddressConflict()
    {
        var exception = new IOException(
            "Failed to bind.",
            new InvalidOperationException(
                "Nested.",
                new SocketException((int)SocketError.AddressAlreadyInUse)));

        Assert.True(StartupFailureClassifier.IsAddressAlreadyInUse(exception));
        Assert.False(StartupFailureClassifier.IsAddressAlreadyInUse(new IOException("Different failure.")));
    }
}
