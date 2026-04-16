using System.Text.Json;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage;
using MediaEngine.Storage.Models;

namespace MediaEngine.Storage.Tests;

/// <summary>
/// Unit tests for the Stage 2 edition pivot configuration, now embedded in
/// <c>config/providers/wikidata_reconciliation.json</c> under the
/// <c>edition_pivot</c> section.
///
/// <para>
/// Verifies round-trip deserialisation, per-media-type lookups, and the
/// coverage invariant that every media type the adapter could reasonably
/// resolve at Stage 2 is either present or deliberately absent (movies,
/// TV, comics skip edition pivot).
/// </para>
/// </summary>
public sealed class Stage2ConfigTests
{
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    // ── EditionPivotConfiguration ────────────────────────────────────────

    [Fact]
    public void EditionPivot_RoundTripsCleanly()
    {
        var json = """
        {
          "$schema_hint": "test fixture",
          "rules": {
            "audiobooks": {
              "work_classes":    ["Q7725634", "Q571"],
              "edition_classes": ["Q122731938", "Q106833962"],
              "prefer_edition":  true
            },
            "books": {
              "work_classes":    ["Q7725634", "Q8261", "Q571"],
              "edition_classes": ["Q3331189"],
              "prefer_edition":  false
            }
          }
        }
        """;

        var config = JsonSerializer.Deserialize<EditionPivotConfiguration>(json, s_jsonOpts);
        Assert.NotNull(config);
        Assert.Equal(2, config!.Rules.Count);

        var books = config.GetRuleFor(MediaType.Books);
        Assert.NotNull(books);
        Assert.Equal(new[] { "Q7725634", "Q8261", "Q571" }, books!.WorkClasses);
        Assert.Equal(new[] { "Q3331189" }, books.EditionClasses);
        Assert.False(books.PreferEdition);

        var audiobooks = config.GetRuleFor(MediaType.Audiobooks);
        Assert.NotNull(audiobooks);
        Assert.True(audiobooks!.PreferEdition);
        Assert.Equal(2, audiobooks.EditionClasses.Count);
    }

    [Fact]
    public void EditionPivot_MissingMediaTypeReturnsNull()
    {
        var config = new EditionPivotConfiguration
        {
            Rules = new()
            {
                ["books"] = new EditionPivotRuleEntry { WorkClasses = ["Q7725634"] },
            },
        };

        // Movies / TV / Comics are intentionally not edition-aware.
        Assert.Null(config.GetRuleFor(MediaType.Movies));
        Assert.Null(config.GetRuleFor(MediaType.TV));
        Assert.Null(config.GetRuleFor(MediaType.Comics));
        Assert.Null(config.GetRuleFor(MediaType.Unknown));
    }

    [Fact]
    public void EditionPivot_ShippedConfigCoversExpectedMediaTypes()
    {
        // Loading the edition_pivot section from wikidata_reconciliation.json —
        // this catches typos, missing keys, and drift between the documented
        // media-type list and the shipped file.
        var root = FindRepoRoot();
        var path = Path.Combine(root, "config", "providers", "wikidata_reconciliation.json");
        Assert.True(File.Exists(path), $"wikidata_reconciliation.json not found at {path}");

        var json = File.ReadAllText(path);
        var reconConfig = JsonSerializer.Deserialize<ReconciliationProviderConfig>(json, s_jsonOpts);
        Assert.NotNull(reconConfig);

        var config = reconConfig!.GetEditionPivotConfiguration();
        Assert.NotNull(config);

        // Books, Audiobooks, Music are edition-aware and must have rules.
        var editionAware = new[] { MediaType.Books, MediaType.Audiobooks, MediaType.Music };
        foreach (var mt in editionAware)
        {
            var rule = config!.GetRuleFor(mt);
            Assert.NotNull(rule);
            Assert.NotEmpty(rule!.WorkClasses);
            Assert.NotEmpty(rule.EditionClasses);
        }

        // Movies, TV, Comics, Unknown are deliberately opted out.
        var optedOut = new[] { MediaType.Movies, MediaType.TV, MediaType.Comics, MediaType.Unknown };
        foreach (var mt in optedOut)
            Assert.Null(config!.GetRuleFor(mt));
    }

    [Fact]
    public void WikidataConfig_IncludesStage3HierarchyAndTvBridgeProperties()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "config", "providers", "wikidata_reconciliation.json");
        Assert.True(File.Exists(path), $"wikidata_reconciliation.json not found at {path}");

        var json = File.ReadAllText(path);
        var reconConfig = JsonSerializer.Deserialize<ReconciliationProviderConfig>(json, s_jsonOpts);
        Assert.NotNull(reconConfig);

        var workProps = reconConfig!.DataExtension.WorkProperties;
        Assert.Contains("P8345", workProps.Core);
        Assert.Contains("P1434", workProps.Core);
        Assert.Contains("P4835", workProps.Bridges);

        var labels = reconConfig.DataExtension.PropertyLabels;
        Assert.Equal("franchise", labels["P8345"]);
        Assert.Equal("fictional_universe", labels["P1434"]);
        Assert.Equal("tvdb_id", labels["P4835"]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(typeof(Stage2ConfigTests).Assembly.Location);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not find repository root (.git directory)");
    }
}
