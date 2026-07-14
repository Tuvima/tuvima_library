using MediaEngine.Domain;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Services;
using MudBlazor;

namespace MediaEngine.Web.Components.LibraryItems;

/// <summary>Static display helpers shared across LibraryItem components.</summary>
public static class LibraryItemHelpers
{
    public static string GetMediaTypeIcon(string? mediaType)
    {
        var upper = mediaType?.ToUpperInvariant();
        if (upper is "UNIVERSE") return Icons.Material.Outlined.AutoAwesome;
        if (upper is "PERSON" or "PEOPLE") return Icons.Material.Outlined.Person;

        var type = MediaTypeClassifier.Classify(mediaType);
        return type switch
        {
            MediaType.Books      => Icons.Material.Outlined.MenuBook,
            MediaType.Audiobooks => Icons.Material.Outlined.Headphones,
            MediaType.Movies     => Icons.Material.Outlined.Movie,
            MediaType.TV         => Icons.Material.Outlined.Tv,
            MediaType.Music      => Icons.Material.Outlined.MusicNote,
            MediaType.Comics     => Icons.Material.Outlined.AutoStories,
            _                    => Icons.Material.Outlined.InsertDriveFile,
        };
    }

    public static string FormatMediaType(string? mediaType)
    {
        // Handle special UI entity types not in the Domain enum
        var upper = mediaType?.ToUpperInvariant();
        if (upper is "PERSON" or "PEOPLE") return "Person";
        if (upper is "UNIVERSE" or "UNIVERSES") return "Universe";
        return MediaTypeClassifier.GetDisplayLabel(mediaType);
    }
}
