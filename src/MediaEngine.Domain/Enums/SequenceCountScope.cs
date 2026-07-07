namespace MediaEngine.Domain.Enums;

/// <summary>
/// Describes what an external total count represents for an ordered container.
/// </summary>
public enum SequenceCountScope
{
    Unknown = 0,
    MainSequence = 1,
    ExtrasIncluded = 2,
    Standalone = 3,
    CollectedEdition = 4,
    BroaderFranchise = 5
}
