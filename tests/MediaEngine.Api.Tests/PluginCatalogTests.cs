using System.Reflection;
using System.Text.Json;
using MediaEngine.Api.Services.Plugins;
using MediaEngine.Plugins;
using MediaEngine.Storage;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Api.Tests;

public sealed class PluginCatalogTests
{
    [Fact]
    public void DynamicPluginManifest_CanBeEditedAndDeleted()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "tuvima-plugin-tests", Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(tempRoot, "config");
        var libraryRoot = Path.Combine(tempRoot, "library");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(libraryRoot);

        try
        {
            using var loader = new ConfigurationDirectoryLoader(configRoot);
            loader.SaveCore(new CoreConfiguration { LibraryRoot = libraryRoot });

            var settingsStore = new PluginSettingsStore(loader);
            var pluginRoot = Path.Combine(libraryRoot, ".data", "plugins", "sample-plugin");
            Directory.CreateDirectory(pluginRoot);

            var testAssemblyPath = Assembly.GetExecutingAssembly().Location;
            File.Copy(testAssemblyPath, Path.Combine(pluginRoot, Path.GetFileName(testAssemblyPath)));
            File.WriteAllText(Path.Combine(pluginRoot, "plugin.json"), """
                {
                  "id": "tuvima.test.dynamic",
                  "name": "Dynamic Test",
                  "version": "1.0.0",
                  "description": "Loaded from plugin.json.",
                  "entry_assembly": "MediaEngine.Api.Tests.dll",
                  "entry_type": "MediaEngine.Api.Tests.PluginCatalogTests+DynamicTestPlugin",
                  "default_settings": {
                    "enabled_feature": true
                  }
                }
                """);

            var catalog = new PluginCatalog([], settingsStore, loader, NullLogger<PluginCatalog>.Instance);
            var plugin = Assert.Single(catalog.List());
            Assert.False(plugin.IsBuiltIn);
            Assert.Equal("Dynamic Test", plugin.Manifest.Name);
            Assert.Equal("1.0.0", plugin.Manifest.Version);

            catalog.SetEnabled("tuvima.test.dynamic", true);
            Assert.True(catalog.Get("tuvima.test.dynamic")!.Enabled);

            var manifestJson = catalog.GetManifestJson("tuvima.test.dynamic");
            Assert.Contains("\"Dynamic Test\"", manifestJson, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(manifestJson);
            var manifest = document.Deserialize<PluginManifest>()!;
            var updatedManifest = manifest with { Version = "1.1.0", Description = "Edited through JSON." };
            catalog.SaveManifestJson("tuvima.test.dynamic", JsonSerializer.Serialize(updatedManifest));

            var updated = catalog.Get("tuvima.test.dynamic")!;
            Assert.Equal("1.1.0", updated.Manifest.Version);
            Assert.Equal("Edited through JSON.", updated.Manifest.Description);

            catalog.DeletePlugin("tuvima.test.dynamic");

            Assert.Empty(catalog.List());
            Assert.False(Directory.Exists(pluginRoot));
            Assert.False(File.Exists(Path.Combine(libraryRoot, ".data", "plugin-config", "tuvima.test.dynamic.json")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    public sealed class DynamicTestPlugin : ITuvimaPlugin
    {
        public PluginManifest Manifest { get; } = new()
        {
            Id = "tuvima.test.dynamic",
            Name = "Assembly Fallback",
            Version = "0.0.1",
            Description = "The editable manifest should override this metadata.",
        };

        public IReadOnlyList<IPluginCapability> CreateCapabilities() => [];
    }
}
