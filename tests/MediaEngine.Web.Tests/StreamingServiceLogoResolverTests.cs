using System.Buffers.Binary;
using System.Text.Json;
using Bunit;
using MediaEngine.Contracts.Details;
using MediaEngine.Web.Components.Details;
using MediaEngine.Web.Services.Branding;
using MudBlazor;
using MudBlazor.Services;

namespace MediaEngine.Web.Tests;

public sealed class StreamingServiceLogoResolverTests
{
    private const string AssetRoot = "src/MediaEngine.Web/wwwroot";
    private const string ServiceLogoRoot = "src/MediaEngine.Web/wwwroot/images/streaming-services";

    [Theory]
    [InlineData("Hulu", "/images/streaming-services/hulu.png")]
    [InlineData("FX on Hulu", "/images/streaming-services/hulu.png")]
    [InlineData("Apple TV+", "/images/streaming-services/apple-tv-plus.png")]
    [InlineData("The Roku Channel", "/images/streaming-services/roku-channel.png")]
    [InlineData("Vudu", "/images/streaming-services/fandango-at-home.png")]
    [InlineData("HBO Max", "/images/streaming-services/max.png")]
    [InlineData("Max", "/images/streaming-services/max.png")]
    public void Resolver_MapsKnownServiceAliases(string label, string expectedPath)
    {
        var resolver = new StreamingServiceLogoResolver();

        Assert.Equal(expectedPath, resolver.ResolveLogoPath(label));
    }

    [Fact]
    public void Resolver_LeavesUnknownLabelsUnmapped()
    {
        var resolver = new StreamingServiceLogoResolver();

        Assert.Null(resolver.ResolveLogoPath("Totally Unknown Network"));
    }

    [Fact]
    public void Manifest_ContainsValidNormalizedPngAssets()
    {
        var root = FindRepoRoot();
        var manifestPath = Path.Combine(root, ServiceLogoRoot, "manifest.json");
        var manifest = JsonSerializer.Deserialize<List<StreamingServiceLogoManifestEntry>>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var expectedServiceIds = new[]
        {
            "netflix", "disney-plus", "hulu", "max", "prime-video", "apple-tv-plus",
            "paramount-plus", "peacock", "tubi", "pluto-tv", "roku-channel", "crunchyroll",
            "youtube", "youtube-tv", "starz", "showtime", "amc-plus", "britbox", "acorn-tv",
            "shudder", "mubi", "criterion-channel", "fandango-at-home", "plex", "kanopy",
            "hoopla", "discovery-plus", "mgm-plus", "dazn", "espn"
        };

        Assert.Equal(expectedServiceIds.Length, manifest.Count);
        Assert.Equal(expectedServiceIds.Length, manifest.Select(entry => entry.ServiceId).Distinct(StringComparer.Ordinal).Count());

        foreach (var expectedServiceId in expectedServiceIds)
        {
            var entry = Assert.Single(manifest, item => item.ServiceId == expectedServiceId);
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
            Assert.NotEmpty(entry.Aliases);
            Assert.Equal(entry.Aliases.Count, entry.Aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal($"/images/streaming-services/{entry.ServiceId}.png", entry.LogoPath);
            Assert.False(string.IsNullOrWhiteSpace(entry.SourceUrl));
            Assert.True(DateTimeOffset.TryParse(entry.RetrievedAt, out _), $"Invalid retrievedAt for {entry.ServiceId}");
            Assert.False(string.IsNullOrWhiteSpace(entry.UsageNotes));

            var assetPath = Path.Combine(root, AssetRoot, entry.LogoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(assetPath), $"Missing logo asset for {entry.ServiceId}: {assetPath}");
            Assert.True(new FileInfo(assetPath).Length > 1_000, $"Logo asset is unexpectedly empty for {entry.ServiceId}");

            var dimensions = ReadPngDimensions(assetPath);
            Assert.InRange(dimensions.Width, 200, 900);
            Assert.InRange(dimensions.Height, 120, 240);
            Assert.Equal(6, ReadPngColorType(assetPath));
        }
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        var header = File.ReadAllBytes(path).AsSpan(0, 26);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, header[..8].ToArray());

        return (
            BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
            BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    private static int ReadPngColorType(string path)
    {
        var header = File.ReadAllBytes(path).AsSpan(0, 26);
        return header[25];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class StreamingServiceLogoManifestEntry
    {
        public string ServiceId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<string> Aliases { get; set; } = [];
        public string LogoPath { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string RetrievedAt { get; set; } = string.Empty;
        public string UsageNotes { get; set; } = string.Empty;
    }
}

public sealed class StreamingServiceHeroRenderTests : TestContext
{
    public StreamingServiceHeroRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void DetailHero_RendersStreamingServiceLogoSeparatelyFromShowLogo()
    {
        var model = new DetailPageViewModel
        {
            Id = "show-1",
            EntityType = DetailEntityType.TvShow,
            PresentationContext = DetailPresentationContext.Watch,
            Title = "Shogun",
            Artwork = new ArtworkSet
            {
                LogoUrl = "/images/show-title-logos/shogun.png",
                HeroArtwork = new HeroArtworkViewModel
                {
                    HasImage = true,
                    Mode = HeroArtworkMode.BackdropWithLogo,
                    Url = "/images/backdrops/shogun.jpg",
                },
            },
            HeroBrand = new HeroBrandViewModel
            {
                Label = "Hulu",
                ImageUrl = "/images/streaming-services/hulu.png",
            },
        };

        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<DetailHero>(1);
            builder.AddAttribute(2, "Model", model);
            builder.CloseComponent();
        });

        Assert.Equal("/images/show-title-logos/shogun.png", cut.Find(".tl-detail-hero__logo").GetAttribute("src"));
        Assert.Equal("/images/streaming-services/hulu.png", cut.Find(".tl-detail-hero-brand img").GetAttribute("src"));
        Assert.Empty(cut.FindAll(".tl-detail-hero-brand-badge img"));
    }
}
