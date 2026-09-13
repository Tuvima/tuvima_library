using MediaEngine.Domain;
using MediaEngine.Domain.Services;
using MediaEngine.Web.Components.Shared;
using MudBlazor;

namespace MediaEngine.Web.Components.LibraryItems;

/// <summary>Static display helpers shared across LibraryItem components.</summary>
public static class LibraryItemHelpers
{
    public static string GetMediaTypeIcon(string? mediaType)
    {
        var upper = mediaType?.ToUpperInvariant();
        if (upper is "UNIVERSE")
        {
            return Icons.Material.Outlined.AutoAwesome;
        }

        if (upper is "PERSON" or "PEOPLE")
        {
            return Icons.Material.Outlined.Person;
        }

        return AppMediaPresentation.IconFor(mediaType);
    }

    public static string FormatMediaType(string? mediaType)
    {
        // Handle special UI entity types not in the Domain enum
        var upper = mediaType?.ToUpperInvariant();
        if (upper is "PERSON" or "PEOPLE")
        {
            return "Person";
        }

        if (upper is "UNIVERSE" or "UNIVERSES")
        {
            return "Universe";
        }

        return MediaTypeClassifier.GetDisplayLabel(mediaType);
    }
}
