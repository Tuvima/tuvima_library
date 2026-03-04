using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model returned by <c>POST /ingestion/library-scan</c> (Great Inhale).
/// Reports how many Hub and Edition records were hydrated from <c>library.xml</c> sidecars.
/// </summary>
public sealed record LibraryScanResultViewModel(
    [property: JsonPropertyName("hubs_upserted")]     int  HubsUpserted,
    [property: JsonPropertyName("editions_upserted")] int  EditionsUpserted,
    [property: JsonPropertyName("errors")]            int  Errors,
    [property: JsonPropertyName("elapsed_ms")]        long ElapsedMs);
