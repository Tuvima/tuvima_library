namespace MediaEngine.Contracts.Settings;

/// <summary>
/// Response promoted from an anonymous type previously returned by
/// <c>GET /settings/providers/health</c>. Property names are deliberately
/// left byte-identical (same casing, no <c>[JsonPropertyName]</c>) to the
/// anonymous object member names they replace, so the wire shape does not
/// change even though this project's serializer applies camelCase naming.
///
/// <para>
/// Note: this intentionally does not follow the snake_case
/// <c>[JsonPropertyName]</c> convention used elsewhere in
/// <c>MediaEngine.Contracts.Settings</c> (e.g. <see cref="LibraryPreferencesSettings"/>,
/// <see cref="SeriesMissingItemPreferenceDto"/>) — that convention was not
/// applied here per the wire-compatibility requirement for this conversion.
/// </para>
/// </summary>
public sealed record ProviderHealthStatusResponse(
    string ProviderId,
    string Status,
    int ConsecutiveFailures,
    string? LastCheckAt,
    string? LastSuccessAt,
    string? LastFailureAt,
    string? LastFailureReason,
    string? NextCheckAt,
    string? DownSince);

/// <summary>
/// Shared "operation acknowledged" response promoted from the anonymous
/// <c>new { saved = true }</c> shape used by several settings save endpoints
/// (hydration, pipelines, media types). The lower-case <c>saved</c> property
/// name matches the original anonymous member exactly — see the
/// wire-compatibility note on <see cref="ProviderHealthStatusResponse"/>.
/// </summary>
public sealed record SettingsSavedResponse(bool saved);

/// <summary>
/// Response promoted from the anonymous <c>new { path = ... }</c> previously
/// returned by <c>POST /settings/providers/{name}/icon</c>. The lower-case
/// <c>path</c> property name matches the original anonymous member exactly —
/// see the wire-compatibility note on <see cref="ProviderHealthStatusResponse"/>.
/// </summary>
public sealed record ProviderIconPathResponse(string path);

/// <summary>
/// One entry in the settings source-of-truth catalog returned by
/// <c>GET /settings/catalog</c>, promoted from an anonymous type. Property
/// names (including the snake_case <c>restart_required</c>) match the
/// original anonymous members exactly — see the wire-compatibility note on
/// <see cref="ProviderHealthStatusResponse"/>.
/// </summary>
public sealed record SettingsCatalogEntryResponse(
    string key,
    string label,
    string source,
    string owner,
    bool editable,
    string role,
    bool restart_required,
    bool deprecated);

/// <summary>
/// Response promoted from the anonymous type previously returned by
/// <c>GET /settings/ui/library-preferences/diagnostics</c>. Property names
/// (snake_case, matching the original anonymous members) are preserved
/// exactly — see the wire-compatibility note on
/// <see cref="ProviderHealthStatusResponse"/>.
/// </summary>
public sealed record LibraryPreferencesDiagnosticsResponse(
    string source_path,
    string sha256,
    DateTime last_modified_at,
    LibraryPreferencesSettings? settings);
