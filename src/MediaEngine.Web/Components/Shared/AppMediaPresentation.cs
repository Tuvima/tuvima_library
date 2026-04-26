using MudBlazor;

namespace MediaEngine.Web.Components.Shared;

public static class AppMediaPresentation
{
    public static string LabelFor(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return "Media";
        }

        var value = mediaType.ToLowerInvariant();
        if (value.Contains("m4b") || value.Contains("audiobook")) return "Audiobook";
        if (value.Contains("book") || value.Contains("epub")) return "Book";
        if (value.Contains("movie")) return "Movie";
        if (value.Contains("tv")) return "TV";
        if (value.Contains("music") || value.Contains("album")) return "Music";
        if (value.Contains("audio")) return "Audio";
        if (value.Contains("comic") || value.Contains("cbz") || value.Contains("cbr")) return "Comic";
        if (value.Contains("video") || value.Contains("mkv") || value.Contains("mp4")) return "Video";

        return mediaType;
    }

    public static string IconFor(string? mediaType)
    {
        var label = LabelFor(mediaType);
        return label switch
        {
            "Audiobook" or "Audio" => Icons.Material.Filled.Headphones,
            "Book" or "Comic" => Icons.Material.Filled.MenuBook,
            "Movie" or "TV" or "Video" => Icons.Material.Filled.Movie,
            "Music" => Icons.Material.Filled.MusicNote,
            _ => Icons.Material.Filled.Folder,
        };
    }

    public static string AccentFor(string? mediaType)
    {
        var label = LabelFor(mediaType);
        return label switch
        {
            "Book" => "var(--tl-status-success)",
            "Audiobook" or "Audio" => "#84CC16",
            "Movie" or "Video" => "var(--tl-status-info)",
            "TV" => "var(--tl-media-video)",
            "Music" => "#1ED760",
            "Comic" => "#7C4DFF",
            _ => "transparent",
        };
    }

    public static AppMediaCardVariant VariantFor(string? mediaType)
    {
        var label = LabelFor(mediaType);
        return label is "Music" or "Audiobook" or "Audio"
            ? AppMediaCardVariant.Square
            : AppMediaCardVariant.Portrait;
    }
}
