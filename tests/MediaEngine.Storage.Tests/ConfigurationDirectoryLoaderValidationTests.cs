using MediaEngine.Storage;
using MediaEngine.Storage.Configuration;
using MediaEngine.Storage.Models;

namespace MediaEngine.Storage.Tests;

public sealed class ConfigurationDirectoryLoaderValidationTests
{
    [Fact]
    public void ValidCoreJson_LoadsSuccessfully()
    {
        using var temp = TempConfig.Create();
        var loader = new ConfigurationDirectoryLoader(temp.Path);

        var core = loader.LoadCore();

        Assert.Equal("2.0", core.SchemaVersion);
    }

    [Fact]
    public void MalformedJson_FallsBackToValidBackup()
    {
        using var temp = TempConfig.Create();
        var corePath = System.IO.Path.Combine(temp.Path, "core.json");
        File.Copy(corePath, corePath + ".bak", overwrite: true);
        File.WriteAllText(corePath, "{");

        var loader = new ConfigurationDirectoryLoader(temp.Path);

        Assert.Equal("2.0", loader.LoadCore().SchemaVersion);
    }

    [Fact]
    public void MalformedJsonWithoutValidBackup_ThrowsClearValidationException()
    {
        using var temp = TempConfig.Create();
        File.WriteAllText(System.IO.Path.Combine(temp.Path, "core.json"), "{");

        var loader = new ConfigurationDirectoryLoader(temp.Path);

        var ex = Assert.Throws<ConfigValidationException>(() => loader.LoadCore());
        Assert.Contains("core.schema.json", ex.SchemaName);
        Assert.Contains("core.json", ex.FilePath);
    }

    [Fact]
    public void InvalidCoreRange_ThrowsClearValidationException()
    {
        using var temp = TempConfig.Create();
        File.WriteAllText(System.IO.Path.Combine(temp.Path, "core.json"), """
            {
              "schema_version": "2.0",
              "database_path": "library.db",
              "server_name": "Tuvima",
              "date_format": "century"
            }
            """);

        var loader = new ConfigurationDirectoryLoader(temp.Path);

        var ex = Assert.Throws<ConfigValidationException>(() => loader.LoadCore());
        Assert.Contains("date_format", ex.Message);
    }

    [Fact]
    public void ProviderMissingNameOrInvalidTimeout_FailsClearly()
    {
        using var temp = TempConfig.Create();
        var providerPath = System.IO.Path.Combine(temp.Path, "providers", "broken.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(providerPath)!);
        File.WriteAllText(providerPath, """
            {
              "enabled": true,
              "weight": 0.5,
              "http_client": { "timeout_seconds": 0 }
            }
            """);

        var loader = new ConfigurationDirectoryLoader(temp.Path);

        var ex = Assert.Throws<ConfigValidationException>(() => loader.LoadProvider("broken"));
        Assert.Contains("name is required", ex.Message);
        Assert.Contains("timeout_seconds", ex.Message);
    }

    [Fact]
    public void SecretValues_AreNotIncludedInValidationException()
    {
        using var temp = TempConfig.Create();
        File.WriteAllText(System.IO.Path.Combine(temp.Path, "core.json"), """
            {
              "schema_version": "2.0",
              "database_path": "library.db",
              "server_name": "Tuvima",
              "client_secret": "do-not-leak",
              "date_format": "bad"
            }
            """);

        var loader = new ConfigurationDirectoryLoader(temp.Path);

        var ex = Assert.Throws<ConfigValidationException>(() => loader.LoadCore());
        Assert.DoesNotContain("do-not-leak", ex.Message);
    }

    [Fact]
    public async Task Watcher_DebouncesValidReloadAndKeepsLastKnownGoodOnInvalidChange()
    {
        using var temp = TempConfig.Create();
        using var loader = new ConfigurationDirectoryLoader(temp.Path);
        var seen = new List<bool>();
        using var signal = new SemaphoreSlim(0);
        loader.ConfigurationChanged += (_, args) =>
        {
            seen.Add(args.Applied);
            signal.Release();
        };

        _ = loader.LoadCore();
        loader.StartWatching();
        var corePath = System.IO.Path.Combine(temp.Path, "core.json");
        File.WriteAllText(corePath, """
            {
              "schema_version": "2.0",
              "database_path": "library.db",
              "server_name": "Reloaded"
            }
            """);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Reloaded", loader.LoadCore().ServerName);

        File.WriteAllText(corePath, "{");
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal("Reloaded", loader.LoadCore().ServerName);
        Assert.Contains(true, seen);
        Assert.Contains(false, seen);
    }

    private sealed class TempConfig : IDisposable
    {
        private TempConfig(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempConfig Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tuvima-config-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(System.IO.Path.Combine(path, "providers"));
            File.WriteAllText(System.IO.Path.Combine(path, "core.json"), System.Text.Json.JsonSerializer.Serialize(new CoreConfiguration()));
            return new TempConfig(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
