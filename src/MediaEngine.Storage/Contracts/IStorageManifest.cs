using MediaEngine.Storage.Models;

namespace MediaEngine.Storage.Contracts;

/// <summary>
/// Defines access methods for the legacy manifest configuration file.
/// Spec: Phase 4 – Interfaces § IStorageManifest
/// </summary>
public interface IStorageManifest
{
    /// <summary>
    /// Loads the legacy manifest from disk.
    /// Falls back to the <c>.bak</c> backup if the primary is missing
    /// or corrupt, restoring the primary in the process.
    /// Throws <see cref="InvalidOperationException"/> if both files are unavailable.
    /// Spec: "MUST attempt to load from .bak before halting."
    /// </summary>
    LegacyManifest Load();

    /// <summary>
    /// Serialises <paramref name="manifest"/> to the legacy manifest file.
    /// Rotates the previous file to a <c>.bak</c> backup first.
    /// </summary>
    void Save(LegacyManifest manifest);
}
