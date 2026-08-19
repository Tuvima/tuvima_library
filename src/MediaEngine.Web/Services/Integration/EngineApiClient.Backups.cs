using MediaEngine.Contracts.System;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    public async Task<IReadOnlyList<BackupArchiveDto>> GetBackupsAsync(CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<List<BackupArchiveDto>>(
                "GET /system/backups", "/system/backups", static () => [], ct: ct);
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /system/backups failed");
            return [];
        }
    }

    public Task<BackupArchiveDto?> CreateBackupAsync(CancellationToken ct = default) =>
        PostAsync<CreateBackupRequest, BackupArchiveDto>(
            "POST /system/backups", "/system/backups", new CreateBackupRequest(), ct: ct);

    public async Task<byte[]?> DownloadBackupAsync(string fileName, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(fileName);
            return await _http.GetByteArrayAsync($"/system/backups/{encoded}", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /system/backups/{FileName} failed", fileName);
            return null;
        }
    }

    public Task<ScheduleRestoreResultDto?> ScheduleRestoreAsync(string fileName, CancellationToken ct = default) =>
        PostAsync<ScheduleRestoreRequest, ScheduleRestoreResultDto>(
            "POST /system/backups/restore",
            "/system/backups/restore",
            new ScheduleRestoreRequest(fileName),
            ct: ct);
}
