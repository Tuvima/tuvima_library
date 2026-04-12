using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model returned by <c>POST /ingestion/library-scan</c> (Great Inhale).
/// Reports how many Collection and Edition records were hydrated from <c>library.xml</c> sidecars.
/// </summary>
public sealed record LibraryScanResultViewModel(
    [property: JsonPropertyName("collections_upserted")]     int  CollectionsUpserted,
    [property: JsonPropertyName("editions_upserted")] int  EditionsUpserted,
    [property: JsonPropertyName("errors")]            int  Errors,
    [property: JsonPropertyName("elapsed_ms")]        long ElapsedMs);
