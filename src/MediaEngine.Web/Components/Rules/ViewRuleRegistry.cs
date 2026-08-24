using MudBlazor;

namespace MediaEngine.Web.Components.Rules;

public static class ViewRuleRegistry
{
    public static RuleBuilderRegistry Instance { get; } = new()
    {
        Domain = "view",
        Eyebrow = "Smart Gallery membership",
        Title = "Build rules",
        Description = "Choose which personal-media items belong in this Smart Gallery.",
        DefaultCategory = "media",
        ShowSortControls = true,
        Capabilities = new(PeopleMetadata: true, PlaceMetadata: true, FaceRecognition: false, SemanticSearch: false, Ocr: false),
        Categories =
        [
            new("media", "Media", Icons.Material.Outlined.PhotoLibrary),
            new("discovery", "People & places", Icons.Material.Outlined.People),
            new("capture", "Capture", Icons.Material.Outlined.PhotoCamera),
            new("library", "My library", Icons.Material.Outlined.Inventory2),
        ],
        SortFields =
        [
            new("captured_date", "Capture date"), new("added_at", "Date added"), new("file_name", "File name"),
            new("duration", "Duration"), new("media_type", "Media type"),
        ],
        Fields =
        [
            Choice("media", "media_type", "Media type", Icons.Material.Outlined.PhotoLibrary, [new("image", "Photos"), new("video", "Videos"), new("audio", "Audio"), new("document", "Documents")]),
            Lookup("media", "file_type", "File type", Icons.Material.Outlined.InsertDriveFile, "view.file-types"),
            Choice("media", "orientation", "Orientation", Icons.Material.Outlined.CropRotate, [new("landscape", "Landscape"), new("portrait", "Portrait"), new("square", "Square")]),
            Text("media", "duration", "Duration", Icons.Material.Outlined.Schedule, RuleOperators.Number),
            Text("capture", "captured_date", "Capture date", Icons.Material.Outlined.CalendarToday,
                [new("eq", "is on"), new("gt", "is after"), new("lt", "is before"), new("between", "is between")]),
            Lookup("discovery", "people", "People", Icons.Material.Outlined.People, "view.people", "people-metadata"),
            Lookup("discovery", "place", "Place", Icons.Material.Outlined.Place, "view.places", "place-metadata"),
            Lookup("library", "device", "Device", Icons.Material.Outlined.Devices, "view.devices"),
            Lookup("library", "tags", "Tags", Icons.Material.Outlined.LocalOffer, "view.tags"),
            Boolean("library", "favorite", "Favorite", Icons.Material.Outlined.FavoriteBorder),
            Lookup("library", "owner", "Owner", Icons.Material.Outlined.PersonOutline, "view.owners"),
            Lookup("library", "source", "Source", Icons.Material.Outlined.FolderOpen, "view.sources"),
        ],
    };

    private static RuleFieldDefinition Choice(string category, string key, string label, string icon, IReadOnlyList<RuleOptionDefinition> options) =>
        new(category, key, label, $"Filter by {label.ToLowerInvariant()}.", icon, [RuleOperators.Is, RuleOperators.IsNot],
            new(RuleValueProviderKind.StaticOptions, $"view.{key}", options));

    private static RuleFieldDefinition Text(string category, string key, string label, string icon, IReadOnlyList<RuleOperatorDefinition> operators) =>
        new(category, key, label, $"Filter by {label.ToLowerInvariant()}.", icon, operators, new(RuleValueProviderKind.Text));

    private static RuleFieldDefinition Lookup(string category, string key, string label, string icon, string providerKey, string? requiredCapability = null) =>
        new(category, key, label, $"Filter by {label.ToLowerInvariant()} metadata.", icon, RuleOperators.Lookup,
            new(RuleValueProviderKind.DeferredLookup, providerKey, SearchPlaceholder: $"Search {label.ToLowerInvariant()}…"), requiredCapability);

    private static RuleFieldDefinition Boolean(string category, string key, string label, string icon) =>
        new(category, key, label, $"Include items based on {label.ToLowerInvariant()} state.", icon, RuleOperators.Boolean, new(RuleValueProviderKind.Boolean));
}
