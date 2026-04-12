namespace MediaEngine.Web.Components.Vault;

/// <summary>Definition of an editable field in the editor panel.</summary>
public sealed class EditorFieldDef
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public bool ReadOnly { get; init; }
}
