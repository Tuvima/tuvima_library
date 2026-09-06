using System.Net.Http.Json;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Progress;
namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    Task<PersonalStatusInfo?> GetPersonalStatusAsync(DetailEntityType type, Guid id, CancellationToken ct = default);
    Task<PersonalStatusCommandResult> ChangePersonalStatusAsync(PersonalStatusRequest request, CancellationToken ct = default);
    Task<PersonalStatusCommandResult> UndoPersonalStatusAsync(Guid commandId, CancellationToken ct = default);
    Task<IReadOnlyList<PersonalStatusHistoryItem>> GetPersonalHistoryAsync(DetailEntityType type, Guid id, CancellationToken ct = default);
}
public sealed partial class EngineApiClient
{
    public Task<PersonalStatusInfo?> GetPersonalStatusAsync(DetailEntityType type, Guid id, CancellationToken ct = default) =>
        GetAsync<PersonalStatusInfo>("Personal status", $"/api/v1/progress/status/{type}/{id:D}", ct: ct);
    public async Task<PersonalStatusCommandResult> ChangePersonalStatusAsync(PersonalStatusRequest request, CancellationToken ct = default) =>
        await PostAsync<PersonalStatusRequest, PersonalStatusCommandResult>("Change personal status", "/api/v1/progress/status", request, ct: ct)
            ?? throw new InvalidOperationException(LastError ?? "Status could not be changed. Refresh and try again.");
    public async Task<PersonalStatusCommandResult> UndoPersonalStatusAsync(Guid commandId, CancellationToken ct = default) =>
        await PostAsync<object, PersonalStatusCommandResult>("Undo personal status", $"/api/v1/progress/status/{commandId:D}/undo", new { }, ct: ct)
            ?? throw new InvalidOperationException(LastError ?? "Undo is unavailable because progress changed.");
    public async Task<IReadOnlyList<PersonalStatusHistoryItem>> GetPersonalHistoryAsync(DetailEntityType type, Guid id, CancellationToken ct = default) =>
        await GetAsync<List<PersonalStatusHistoryItem>>("Personal status history", $"/api/v1/progress/status/{type}/{id:D}/history", ct: ct)
            ?? throw new InvalidOperationException(LastError ?? "History could not be loaded.");
}
