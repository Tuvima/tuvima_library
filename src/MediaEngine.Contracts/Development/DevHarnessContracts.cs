namespace MediaEngine.Contracts.Development;

/// <summary>
/// Development-only reset details embedded in the reingestion and full-test responses.
/// The enum intentionally retains its numeric JSON representation.
/// </summary>
public sealed record DevHarnessResetResponse(
    DevHarnessWipeScope Scope,
    IReadOnlyList<string> Details);

public enum DevHarnessWipeScope
{
    GeneratedState,
    Full,
}

public sealed record DevHarnessWipeResponse(
    [property: global::System.Text.Json.Serialization.JsonPropertyName("message")] string Message,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("wipe_scope")] string WipeScope,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("details")] IReadOnlyList<string> Details);

public sealed record DevHarnessReingestResponse(
    [property: global::System.Text.Json.Serialization.JsonPropertyName("message")] string Message,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("reset")] DevHarnessResetResponse Reset,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("scanned_directories")] string[] ScannedDirectories,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("source_count")] int SourceCount,
    [property: global::System.Text.Json.Serialization.JsonPropertyName("fsw_paused")] bool FswPaused);
