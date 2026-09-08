using MediaEngine.Api.Endpoints;
using MediaEngine.Domain.Configuration;

namespace MediaEngine.Api.Tests;

public sealed class LibraryViewRootChangeTests
{
    [Fact]
    public void RootChangeCannotAbandonExistingPersonalFiles()
    {
        var directory = Directory.CreateTempSubdirectory("tuvima-view-root-test-");
        try
        {
            LibrariesConfiguration Config(string relative) => new()
            {
                StorageLocations = [new ServerStorageLocationConfig { Id = "media", Path = directory.FullName, AllowWrite = true }],
                ViewStorage = new ViewStorageConfig { StorageLocationId = "media", RelativeRoot = relative },
            };
            var current = Config("View");
            var proposed = Config("NewView");
            Assert.Null(SettingsEndpoints.ValidateViewRootChange(current, proposed));
            var root = Directory.CreateDirectory(Path.Combine(directory.FullName, "View", "profiles"));
            var original = Path.Combine(root.FullName, "owned.txt");
            File.WriteAllText(original, "keep this");
            Assert.Contains("leave those files behind", SettingsEndpoints.ValidateViewRootChange(current, proposed));
            Assert.Null(SettingsEndpoints.ValidateViewRootChange(current, Config("View")));
            Assert.Equal("keep this", File.ReadAllText(original));
            Assert.False(Directory.Exists(Path.Combine(directory.FullName, "NewView")));
        }
        finally { directory.Delete(recursive: true); }
    }
}
