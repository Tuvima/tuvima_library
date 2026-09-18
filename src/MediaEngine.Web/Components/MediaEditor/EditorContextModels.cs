namespace MediaEngine.Web.Components.MediaEditor;

public sealed record EditorContextOption(
    Guid EntityId,
    string Label,
    string Title,
    string? Subtitle,
    bool IsActive,
    bool IsEnabled);

public sealed record EditorContextLevel(
    string Label,
    string NodeKind,
    string Title,
    string? Subtitle,
    Guid? EntityId,
    bool IsActive,
    bool CanOpen,
    bool ShowSelector,
    IReadOnlyList<EditorContextOption> Options);
