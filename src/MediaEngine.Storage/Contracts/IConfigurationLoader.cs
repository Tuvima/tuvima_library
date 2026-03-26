using MediaEngine.Storage.Models;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Granular access to the multi-file configuration directory.
///
/// <para>
/// Replaces the monolithic <see cref="IStorageManifest"/> for new code.
/// Each configuration concern has its own load/save pair; universe and
/// provider configs are accessed by name.
/// </para>
///
/// <para>
/// For backward compatibility, implementations also implement
/// <see cref="IStorageManifest"/> — the <see cref="IStorageManifest.Load"/>
/// method assembles all individual files into a composite
/// <see cref="LegacyManifest"/>.
/// </para>
/// </summary>
public interface IConfigurationLoader
{
    // ── Core ─────────────────────────────────────────────────────────────────

    /// <summary>Load core settings (paths, schema version, organisation template).</summary>
    CoreConfiguration LoadCore();

    /// <summary>Persist core settings to <c>config/core.json</c>.</summary>
    void SaveCore(CoreConfiguration config);

    // ── Scoring ──────────────────────────────────────────────────────────────

    /// <summary>Load scoring thresholds and decay parameters.</summary>
    ScoringSettings LoadScoring();

    /// <summary>Persist scoring settings to <c>config/scoring.json</c>.</summary>
    void SaveScoring(ScoringSettings settings);

    // ── Maintenance ──────────────────────────────────────────────────────────

    /// <summary>Load maintenance settings (retention, vacuum, sync).</summary>
    MaintenanceSettings LoadMaintenance();

    /// <summary>Persist maintenance settings to <c>config/maintenance.json</c>.</summary>
    void SaveMaintenance(MaintenanceSettings settings);

    // ── Hydration Pipeline ──────────────────────────────────────────────

    /// <summary>Load hydration pipeline settings (stage concurrency, timeouts, thresholds).</summary>
    HydrationSettings LoadHydration();

    /// <summary>Persist hydration settings to <c>config/hydration.json</c>.</summary>
    void SaveHydration(HydrationSettings settings);

    // ── Provider Slots ──────────────────────────────────────────────────────

    /// <summary>Load provider slot assignments per media type from <c>config/slots.json</c>.</summary>
    ProviderSlotConfiguration LoadSlots();

    /// <summary>Persist provider slot assignments to <c>config/slots.json</c>.</summary>
    void SaveSlots(ProviderSlotConfiguration slots);

    // ── Disambiguation ─────────────────────────────────────────────────────

    /// <summary>Load media type disambiguation settings from <c>config/disambiguation.json</c>.</summary>
    DisambiguationSettings LoadDisambiguation();

    /// <summary>Persist disambiguation settings to <c>config/disambiguation.json</c>.</summary>
    void SaveDisambiguation(DisambiguationSettings settings);

    // ── Transcoding ──────────────────────────────────────────────────────────

    /// <summary>Load FFmpeg and transcoding settings from <c>config/transcoding.json</c>.</summary>
    TranscodingSettings LoadTranscoding();

    /// <summary>Persist transcoding settings to <c>config/transcoding.json</c>.</summary>
    void SaveTranscoding(TranscodingSettings settings);

    // ── Media Types ──────────────────────────────────────────────────────────

    /// <summary>Load media type definitions from <c>config/media_types.json</c>.</summary>
    MediaTypeConfiguration LoadMediaTypes();

    /// <summary>Persist media type definitions to <c>config/media_types.json</c>.</summary>
    void SaveMediaTypes(MediaTypeConfiguration config);

    // ── Library Folders ───────────────────────────────────────────────────

    /// <summary>Load library folder definitions from <c>config/libraries.json</c>.</summary>
    LibrariesConfiguration LoadLibraries();

    // ── Field Priorities ───────────────────────────────────────────────────

    /// <summary>Load per-field provider priority overrides from <c>config/field_priorities.json</c>.</summary>
    FieldPriorityConfiguration LoadFieldPriorities();

    /// <summary>Persist field priority overrides to <c>config/field_priorities.json</c>.</summary>
    void SaveFieldPriorities(FieldPriorityConfiguration config);

    // ── Providers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Load a single provider's configuration by name.
    /// Returns <c>null</c> if the file does not exist or is corrupt.
    /// </summary>
    ProviderConfiguration? LoadProvider(string name);

    /// <summary>
    /// Persist a provider configuration to <c>config/providers/{name}.json</c>.
    /// The filename is derived from <see cref="ProviderConfiguration.Name"/>.
    /// </summary>
    void SaveProvider(ProviderConfiguration config);

    /// <summary>
    /// Load all provider configuration files from <c>config/providers/</c>.
    /// Files that fail to deserialize are silently skipped.
    /// </summary>
    IReadOnlyList<ProviderConfiguration> LoadAllProviders();

    // ── AI Settings ──────────────────────────────────────────────────────────

    /// <summary>Load AI settings (models, features, scheduling) from <c>config/ai.json</c>.</summary>
    T? LoadAi<T>() where T : class;

    /// <summary>Persist AI settings to <c>config/ai.json</c>.</summary>
    void SaveAi<T>(T settings) where T : class;

    // ── Generic (universe, etc.) ─────────────────────────────────────────────

    /// <summary>
    /// Load a typed configuration from a named subdirectory.
    /// Used for universe knowledge models and any future extensible config sections.
    /// Returns <c>null</c> if the file does not exist or is corrupt.
    /// </summary>
    /// <typeparam name="T">The configuration model type to deserialize.</typeparam>
    /// <param name="subdirectory">Subdirectory within the config root (e.g. <c>"universe"</c>).</param>
    /// <param name="name">Filename stem without extension (e.g. <c>"wikidata"</c>).</param>
    T? LoadConfig<T>(string subdirectory, string name) where T : class;

    /// <summary>
    /// Persist a typed configuration to a named subdirectory.
    /// Creates the subdirectory if it does not exist.
    /// </summary>
    void SaveConfig<T>(string subdirectory, string name, T config) where T : class;
}
