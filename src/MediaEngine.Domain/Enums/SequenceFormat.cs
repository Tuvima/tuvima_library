namespace MediaEngine.Domain.Enums;

/// <summary>
/// Media-agnostic placement format for items that live inside an ordered container.
/// </summary>
public enum SequenceFormat
{
    Unknown = 0,
    Standard = 1,
    Annual = 2,
    Special = 3,
    OneShot = 4,
    CollectedEdition = 5,
    Omnibus = 6,
    Compilation = 7,
    BonusDisc = 8,
    TvSpecial = 9
}
