namespace MediaEngine.Domain.Enums;

/// <summary>
/// Lifecycle status of a <see cref="Entities.MediaAsset"/>.
/// Spec: Phase 2 – Failure Handling.
/// </summary>
public enum AssetStatus
{
    /// <summary>Asset is linked to exactly one Edition with a verified hash.</summary>
    Normal,

    /// <summary>
    /// Multiple Works claim this asset.
    /// The system MUST prevent automatic Hub assignment until resolved.
    /// Spec: "flag the MediaAsset as Conflicted and prevent automatic Hub assignment."
    /// </summary>
    Conflicted,

    /// <summary>
    /// Asset could not be linked to any Work.
    /// MUST be assigned to the System-Default Hub for manual triage.
    /// Spec: "Assets that fail to link to a Work MUST be assigned to a System-Default Hub."
    /// </summary>
    Orphaned,
}
