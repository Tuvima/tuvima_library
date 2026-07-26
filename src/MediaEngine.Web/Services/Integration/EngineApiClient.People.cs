using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MediaEngine.Contracts.Display;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Models;
using MediaEngine.Contracts.Settings;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Branding;
using MediaEngine.Web.Services.Integration.Clients;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    // -- GET /persons (libraryItem list) ------------------------------------

    public async Task<IReadOnlyList<PersonListItemDto>?> GetPersonsAsync(
        string? role = null, int offset = 0, int limit = 200, CancellationToken ct = default)
    {
        try
        {
            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit <= 0 ? 200 : limit, 1, 500);
            var url = $"/persons?offset={safeOffset}&limit={safeLimit}";
            if (!string.IsNullOrEmpty(role))
                url += $"&role={Uri.EscapeDataString(role)}";
            var payload = await _http.GetFromJsonAsync<JsonElement>(url, ct);
            List<PersonListItemDto>? results;
            if (payload.ValueKind == JsonValueKind.Array)
            {
                results = payload.Deserialize<List<PersonListItemDto>>();
            }
            else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("items", out var items))
            {
                results = items.Deserialize<List<PersonListItemDto>>();
            }
            else
            {
                results = [];
            }
            if (results is not null)
            {
                foreach (var p in results)
                {
                    // Build absolute headshot URL from the Engine base address
                    if (p.HasLocalHeadshot || !string.IsNullOrEmpty(p.HeadshotUrl))
                        p.HeadshotUrl = AbsoluteUrl($"/persons/{p.Id}/headshot");
                }
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons failed");
            return null;
        }
    }

    // -- GET /persons/by-collection/{collectionId} -------------------------------------

    public async Task<List<PersonViewModel>> GetPersonsByRoleAsync(
        string role, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 500);
            var payload = await _http.GetFromJsonAsync<JsonElement>(
                $"/persons?role={Uri.EscapeDataString(role)}&limit={safeLimit}", ct);
            List<PersonRaw>? raw;
            if (payload.ValueKind == JsonValueKind.Array)
            {
                raw = payload.Deserialize<List<PersonRaw>>();
            }
            else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("items", out var items))
            {
                raw = items.Deserialize<List<PersonRaw>>();
            }
            else
            {
                raw = [];
            }
            return raw?.Select(p =>
            {
                var headshotUrl = ResolvePersonHeadshotUrl(p);
                return new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = headshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = headshotUrl,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            };
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons?role={Role} failed", role);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<PersonViewModel>> GetPersonsByCollectionAsync(
        Guid collectionId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<PersonRaw>>(
                $"/persons/by-collection/{collectionId}", ct);
            return raw?.Select(p =>
            {
                var headshotUrl = ResolvePersonHeadshotUrl(p);
                return new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = headshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = headshotUrl,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            };
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/by-collection/{CollectionId} failed", collectionId);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<PersonViewModel>> GetPersonsByWorkAsync(
        Guid workId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<PersonRaw>>(
                $"/persons/by-work/{workId}", ct);
            return raw?.Select(p =>
            {
                var headshotUrl = ResolvePersonHeadshotUrl(p);
                return new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = headshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = headshotUrl,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            };
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/by-work/{WorkId} failed", workId);
            LastError = ex.Message;
            return [];
        }
    }

    // -- GET /persons/role-counts ------------------------------------------

    public async Task<Dictionary<string, int>> GetPersonRoleCountsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<Dictionary<string, int>>("/persons/role-counts", ct);
            return result ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/role-counts failed");
            return new();
        }
    }

    // -- GET /persons/presence?ids=... -------------------------------------

    public async Task<Dictionary<string, Dictionary<string, int>>> GetPersonPresenceAsync(
        IEnumerable<Guid> personIds, CancellationToken ct = default)
    {
        try
        {
            var ids = string.Join(",", personIds);
            var result = await _http.GetFromJsonAsync<Dictionary<string, Dictionary<string, int>>>(
                $"/persons/presence?ids={ids}", ct);
            return result ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/presence failed");
            return new();
        }
    }

    // -- GET /collections/{id}/related -------------------------------------------------

    public async Task<RelatedCollectionsViewModel?> GetRelatedCollectionsAsync(
        Guid collectionId, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<RelatedCollectionsRaw>(
                $"/collections/{collectionId}/related?limit={limit}", ct);
            if (raw is null) return null;
            return new RelatedCollectionsViewModel
            {
                SectionTitle = raw.SectionTitle,
                Reason       = raw.Reason,
                Collections         = raw.Collections.Select(MapCollection).ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/{CollectionId}/related failed", collectionId);
            LastError = ex.Message;
            return null;
        }
    }

    // -- GET /persons/{id} (detail) ------------------------------------------

    public async Task<PersonDetailViewModel?> GetPersonDetailAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<PersonDetailRaw>(
                $"/persons/{personId}", ct);
            if (raw is null) return null;
            return new PersonDetailViewModel
            {
                Id               = raw.Id,
                Name             = raw.Name ?? string.Empty,
                Roles            = raw.Roles ?? [],
                HeadshotUrl      = raw.HeadshotUrl,
                HasLocalHeadshot = raw.HasLocalHeadshot,
                LocalHeadshotUrl = (raw.HasLocalHeadshot || !string.IsNullOrEmpty(raw.HeadshotUrl)) ? AbsoluteUrl($"/persons/{raw.Id}/headshot") : null,
                Biography        = raw.Biography,
                Occupation       = raw.Occupation,
                DateOfBirth      = raw.DateOfBirth,
                DateOfDeath      = raw.DateOfDeath,
                PlaceOfBirth     = raw.PlaceOfBirth,
                PlaceOfDeath     = raw.PlaceOfDeath,
                Nationality      = raw.Nationality,
                WikidataQid      = raw.WikidataQid,
                Instagram        = raw.Instagram,
                Twitter          = raw.Twitter,
                TikTok           = raw.TikTok,
                Mastodon         = raw.Mastodon,
                Website          = raw.Website,
                IsGroup          = raw.IsGroup,
                GroupMembers     = raw.GroupMembers?.Select(MapGroupMember).ToList() ?? [],
                MemberOfGroups   = raw.MemberOfGroups?.Select(MapGroupMember).ToList() ?? [],
                BannerUrl        = raw.BannerUrl is not null ? AbsoluteUrl(raw.BannerUrl) : null,
                BackgroundUrl    = raw.BackgroundUrl is not null ? AbsoluteUrl(raw.BackgroundUrl) : null,
                LogoUrl          = raw.LogoUrl is not null ? AbsoluteUrl(raw.LogoUrl) : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/{PersonId} failed", personId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<List<PersonLibraryCreditViewModel>> GetPersonLibraryCreditsAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            var credits = await _http.GetFromJsonAsync<List<PersonLibraryCreditViewModel>>(
                $"/persons/{personId}/library-credits", ct);

            NormalizePersonLibraryCredits(credits);
            return credits ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/{PersonId}/library-credits failed", personId);
            LastError = ex.Message;
            return [];
        }
    }

    // -- GET /persons/{id}/works -----------------------------------------------

    public async Task<List<CollectionViewModel>> GetWorksByPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<CollectionRaw>>(
                $"/persons/{personId}/works", ct);
            return raw?.Select(MapCollection).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/{PersonId}/works failed", personId);
            LastError = ex.Message;
            return [];
        }
    }

    // -- GET /persons/{id}/aliases --------------------------------------------

    /// <inheritdoc/>
    public async Task<PersonAliasesResponseDto?> GetPersonAliasesAsync(Guid personId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"persons/{personId}/aliases", ct);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<PersonAliasesResponseDto>(cancellationToken: ct);
            if (result is not null)
            {
                foreach (var alias in result.Aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias.HeadshotUrl))
                        alias.HeadshotUrl = AbsoluteUrl(alias.HeadshotUrl);
                }
            }

            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/{PersonId}/aliases failed", personId);
            return null;
        }
    }

}
