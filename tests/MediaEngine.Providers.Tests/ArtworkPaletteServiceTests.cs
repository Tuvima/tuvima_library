using MediaEngine.Domain.Models;
using MediaEngine.Providers.Services;
using MediaEngine.Providers.Helpers;
using SkiaSharp;

namespace MediaEngine.Providers.Tests;

public sealed class ArtworkPaletteServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"tuvima_palette_{Guid.NewGuid():N}");

    public ArtworkPaletteServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task GeneratePaletteAsync_ReturnsFallbackWhenNoImagesAreUsable()
    {
        var service = new ArtworkPaletteService();

        var palette = await service.GeneratePaletteAsync(
        [
            new ArtworkPaletteSource { Id = "missing", LocalPath = Path.Combine(_tempRoot, "missing.jpg") },
        ]);

        Assert.Equal("#0c0a14", palette.BaseColor);
        Assert.Equal("#070812", palette.BaseColorDark);
        Assert.Equal("#7c5cff", palette.AccentColor);
        Assert.True(palette.IsDarkSafe);
        Assert.Contains("--art-bg-base", palette.CssVariables.Keys);
        Assert.Contains("linear-gradient", palette.CssGradient);
        Assert.Contains("radial-gradient", palette.CssRadialGlow);
    }

    [Fact]
    public async Task GeneratePaletteAsync_DarkensLightArtworkForWhiteText()
    {
        var path = WriteImage("light.jpg", SKColors.White, SKColors.LightGoldenrodYellow);
        var service = new ArtworkPaletteService();

        var palette = await service.GeneratePaletteAsync(
        [
            new ArtworkPaletteSource { Id = "light", LocalPath = path },
        ]);

        Assert.True(palette.IsDarkSafe);
        Assert.True(palette.ContrastScore >= 7d);
        Assert.NotEqual("#FFFFFF", palette.BaseColor, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith("#", palette.BaseColor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratePaletteAsync_UsesTuvimaBaseForMuddyGrayArtwork()
    {
        var path = WriteSolidPng("gray.png", new SKColor(116, 116, 112));
        var service = new ArtworkPaletteService();

        var palette = await service.GeneratePaletteAsync(
        [
            new ArtworkPaletteSource { Id = "gray", LocalPath = path },
        ]);

        Assert.Equal("#0c0a14", palette.BaseColor);
        Assert.Equal("#070812", palette.BaseColorDark);
        Assert.True(palette.IsDarkSafe);
    }

    [Fact]
    public async Task GeneratePaletteAsync_BalancesMultipleImagesDeterministically()
    {
        var amber = WriteImage("amber.jpg", new SKColor(230, 124, 42), new SKColor(70, 24, 12));
        var violet = WriteImage("violet.jpg", new SKColor(124, 92, 255), new SKColor(24, 14, 70));
        var teal = WriteImage("teal.jpg", new SKColor(32, 190, 180), new SKColor(7, 44, 52));
        var service = new ArtworkPaletteService();
        var sources = new[]
        {
            new ArtworkPaletteSource { Id = "b", LocalPath = violet },
            new ArtworkPaletteSource { Id = "a", LocalPath = amber },
            new ArtworkPaletteSource { Id = "c", LocalPath = teal },
        };

        var first = await service.GeneratePaletteAsync(sources);
        var second = await service.GeneratePaletteAsync(sources.Reverse().ToList());

        Assert.Equal(first.BaseColor, second.BaseColor);
        Assert.Equal(first.AccentColor, second.AccentColor);
        Assert.Equal(first.SecondaryGlowColor, second.SecondaryGlowColor);
        Assert.True(first.IsDarkSafe);
        Assert.NotEqual(first.GlowColor, first.SecondaryGlowColor);
    }

    [Fact]
    public async Task GeneratePaletteAsync_ReducesSkinToneDominance()
    {
        var poster = WriteImage("poster.jpg", new SKColor(210, 142, 96), new SKColor(58, 104, 214));
        var service = new ArtworkPaletteService();

        var palette = await service.GeneratePaletteAsync(
        [
            new ArtworkPaletteSource { Id = "poster", LocalPath = poster },
        ]);

        Assert.DoesNotContain("210, 142, 96", palette.GlowColor, StringComparison.Ordinal);
        Assert.True(palette.IsDarkSafe);
        Assert.Contains("--art-bg-glow", palette.CssVariableStyle);
    }

    [Fact]
    public void BackdropLeftEdgePalette_SamplesVerticalRegionsFromBackdropLeftSide()
    {
        using var bitmap = new SKBitmap(200, 120);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Magenta);
        using var paint = new SKPaint();
        paint.Color = new SKColor(24, 54, 88);
        canvas.DrawRect(new SKRect(0, 0, 48, 40), paint);
        paint.Color = new SKColor(46, 76, 102);
        canvas.DrawRect(new SKRect(0, 40, 48, 80), paint);
        paint.Color = new SKColor(18, 36, 42);
        canvas.DrawRect(new SKRect(0, 80, 48, 120), paint);

        var palette = ArtworkVariantHelper.ExtractBackdropLeftEdgePalette(bitmap);

        Assert.Equal("#183658", palette.PrimaryHex, ignoreCase: true);
        Assert.Equal("#2E4C66", palette.SecondaryHex, ignoreCase: true);
        Assert.Equal("#12242A", palette.AccentHex, ignoreCase: true);
    }

    private string WriteImage(string name, SKColor primary, SKColor secondary)
    {
        var path = Path.Combine(_tempRoot, name);
        using var bitmap = new SKBitmap(96, 96);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(primary);
        using var paint = new SKPaint { Color = secondary };
        canvas.DrawRect(new SKRect(0, 0, 48, 96), paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
        return path;
    }

    private string WriteSolidPng(string name, SKColor color)
    {
        var path = Path.Combine(_tempRoot, name);
        using var bitmap = new SKBitmap(96, 96);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
        return path;
    }
}
