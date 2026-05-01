namespace MediaEngine.Domain.Enums;

/// <summary>
/// External work classes that need app-level throttling across all library roots.
/// </summary>
public enum EnrichmentWorkKind
{
    RetailProvider,
    Wikidata,
    Fanart,
    WriteBack,
}
