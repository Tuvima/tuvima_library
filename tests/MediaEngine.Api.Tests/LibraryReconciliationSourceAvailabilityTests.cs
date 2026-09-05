using MediaEngine.Api.Services;

namespace MediaEngine.Api.Tests;

public sealed class LibraryReconciliationSourceAvailabilityTests
{
    [Fact]
    public void ConfiguredSourceAvailability_RequiresAnExistingRoot()
    {
        var existing = Path.Combine(Path.GetTempPath(), $"tuvima-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(existing);
        try
        {
            Assert.True(LibraryReconciliationService.IsConfiguredSourceAvailable(existing));
            Assert.False(LibraryReconciliationService.IsConfiguredSourceAvailable(Path.Combine(existing, "offline")));
            Assert.False(LibraryReconciliationService.IsConfiguredSourceAvailable(string.Empty));
        }
        finally
        {
            Directory.Delete(existing);
        }
    }
}
