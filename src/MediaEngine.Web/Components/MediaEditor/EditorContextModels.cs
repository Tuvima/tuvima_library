namespace MediaEngine.Web.Components.MediaEditor;

public sealed record EditorContextOption(
    Guid EntityId,
    string Label,
    string Title,
    string? Subtitle,
    bool IsActive,
    bool IsEnabled,
    string? ArtworkUrl = null,
    string? RetailStatus = null,
    string? CanonicalStatus = null,
    bool IsTextOnly = false);

public sealed record EditorContextLevel(
    string Label,
    string NodeKind,
    string Title,
    string? Subtitle,
    Guid? EntityId,
    bool IsActive,
    bool CanOpen,
    bool ShowSelector,
    IReadOnlyList<EditorContextOption> Options,
    string? ArtworkUrl = null,
    string? RetailStatus = null,
    string? CanonicalStatus = null,
    bool IsTextOnly = false);
