using System.Text.Json;
using MediaEngine.Contracts.Display;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Playback;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Models;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    /// <summary>GET /api/v1/display/home — cross-platform consumer display model for Home.</summary>
    Task<DisplayPageDto?> GetDisplayHomeAsync(Guid? profileId = null, CancellationToken ct = default);

    /// <summary>GET /api/v1/display/browse — cross-platform consumer display model for Watch, Read, Listen, and browse surfaces.</summary>
    Task<DisplayPageDto?> GetDisplayBrowseAsync(
        string? lane = null,
        string? mediaType = null,
        string? grouping = null,
        string? search = null,
        int? offset = null,
        int? limit = null,
        bool? includeCatalog = null,
        Guid? profileId = null,
        CancellationToken ct = default,
        string? genres = null,
        string? creator = null,
        string? status = null,
        string? year = null,
        string? sort = null);

    /// <summary>GET /api/v1/display/continue - compact in-progress cards for one lane and optional media type.</summary>
    Task<DisplayPageDto?> GetDisplayContinueAsync(
        string? lane = null,
        string? mediaType = null,
        int? limit = null,
        CancellationToken ct = default);

}
