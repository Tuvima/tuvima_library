using SkiaSharp;

namespace MediaEngine.Api.DevSupport;

/// <summary>Offline, synthetic images for exercising dense Places stories, not real location evidence.</summary>
public static class ViewDensityFixtures
{
    public sealed record Sample(string FileName, string City, string LocationName, double Latitude,
        double Longitude, DateTimeOffset CapturedAt, int Number, int Width, int Height);

    public static IReadOnlyList<Sample> Create()
    {
        var samples = new List<Sample>();
        foreach (var place in new[]
        {
            (City: "Seattle", Name: "Seattle, United States", Lat: 47.6062, Lon: -122.3321, Count: 180),
            (City: "Tokyo", Name: "Tokyo, Japan", Lat: 35.6595, Lon: 139.7005, Count: 72),
            (City: "Paris", Name: "Paris, France", Lat: 48.8584, Lon: 2.2945, Count: 36)
        })
        {
            for (var i = 0; i < place.Count; i++)
            {
                // Most images share one day; the rest exercise month/year headings and sparse dates.
                var date = i < place.Count * 2 / 3
                    ? new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero).AddMinutes(i * 3)
                    : new DateTimeOffset(2020 + i % 7, 1 + i % 9, 1 + i % 27, 12, i % 60, 0, TimeSpan.Zero);
                var (width, height) = (i % 4) switch { 0 => (480, 720), 1 => (720, 480), 2 => (600, 600), _ => (960, 400) };
                samples.Add(new($"density-v1-{place.City.ToLowerInvariant()}-{i + 1:D3}.png",
                    place.City, place.Name, place.Lat, place.Lon, date, i + 1, width, height));
            }
        }
        return samples;
    }

    public static byte[] Render(Sample sample)
    {
        using var bitmap = new SKBitmap(sample.Width, sample.Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor((byte)(30 + sample.Number * 17 % 100), 30, (byte)(65 + sample.Number * 29 % 150)));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(160, 110, 245) };
        // A varying geometric skyline keeps neighboring thumbnails easy to distinguish.
        for (var column = 0; column < 12; column++)
        {
            var height = 40 + (sample.Number * 31 + column * 47) % (sample.Height / 2);
            canvas.DrawRect(column * sample.Width / 12f, sample.Height - height,
                sample.Width / 12f - 5, height, paint);
        }
        paint.Color = SKColors.White;
        using var font = new SKFont(SKTypeface.Default, 26);
        canvas.DrawText("SYNTHETIC TEST IMAGE", 24, 48, SKTextAlign.Left, font, paint);
        canvas.DrawText($"{sample.City} / {sample.Number:D3}", 24, 90, SKTextAlign.Left, font, paint);
        canvas.DrawText(sample.CapturedAt.ToString("yyyy-MM-dd"), 24, 130, SKTextAlign.Left, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
