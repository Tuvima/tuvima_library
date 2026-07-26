using System.Text.Json;

namespace MediaEngine.Domain.Services;

/// <summary>
/// Shared, cached <see cref="JsonSerializerOptions"/> instances for the small
/// set of serialization shapes reused across the Engine and Dashboard.
/// <see cref="JsonSerializerOptions"/> caches reflection/serialization metadata
/// internally per instance, so allocating a fresh instance at every call site
/// (as roughly a dozen sites previously did) discards that cache on every call;
/// sharing these statics restores it. Sites that need configuration beyond what
/// is offered here (custom converters, naming policies, source-generated
/// contexts, etc.) should keep their own local cached static and document why
/// in an XML comment — see the guardrail allowlist in
/// <c>SharedPrimitivesGuardrailTests</c> for the current list of such sites.
/// </summary>
public static class MediaEngineJson
{
    /// <summary>
    /// Web-defaults options (camelCase property names, case-insensitive property
    /// matching, relaxed JSON escaping) — equivalent to
    /// <c>new JsonSerializerOptions(JsonSerializerDefaults.Web)</c>.
    /// </summary>
    public static JsonSerializerOptions Web { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Default options with <see cref="JsonSerializerOptions.WriteIndented"/>
    /// enabled, for human-readable output (reports, debug dumps, pretty-printed
    /// config snapshots).
    /// </summary>
    public static JsonSerializerOptions Indented { get; } = new() { WriteIndented = true };

    /// <summary>
    /// Default options with <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/>
    /// enabled, for deserializing loosely-cased JSON (stored rule predicates,
    /// externally supplied payloads).
    /// </summary>
    public static JsonSerializerOptions CaseInsensitive { get; } = new() { PropertyNameCaseInsensitive = true };
}
