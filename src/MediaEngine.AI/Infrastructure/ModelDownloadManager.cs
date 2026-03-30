using System.Security.Cryptography;
using MediaEngine.AI.Configuration;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace MediaEngine.AI.Infrastructure;

/// <summary>
/// Downloads AI model files from configured URLs with progress reporting and SHA-256 validation.
/// </summary>
public sealed class ModelDownloadManager : IModelDownloadManager
{
    private readonly AiSettings _settings;
    private readonly ModelInventory _inventory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ModelDownloadManager> _logger;

    private readonly Dictionary<AiModelRole, CancellationTokenSource> _activeDownloads = new();
    private readonly Dictionary<AiModelRole, (long Downloaded, long Total)> _progress = new();
    private readonly object _lock = new();

    public ModelDownloadManager(
        AiSettings settings,
        ModelInventory inventory,
        IHttpClientFactory httpClientFactory,
        IEventPublisher eventPublisher,
        ILogger<ModelDownloadManager> logger)
    {
        _settings = settings;
        _inventory = inventory;
        _httpClientFactory = httpClientFactory;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task StartDownloadAsync(AiModelRole role, CancellationToken ct = default)
    {
        var definition = _inventory.GetDefinition(role);
        if (string.IsNullOrEmpty(definition.DownloadUrl))
        {
            _logger.LogWarning("No download URL configured for model role {Role}", role);
            return;
        }

        // Cancel any existing download for this role.
        await CancelDownloadAsync(role, ct);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_lock) { _activeDownloads[role] = cts; }

        _inventory.SetState(role, AiModelState.Downloading);
        _logger.LogInformation("Starting download for {Role}: {Url}", role, definition.DownloadUrl);

        _ = Task.Run(async () =>
        {
            try
            {
                await DownloadCoreAsync(role, definition, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Download cancelled for {Role}", role);
                _inventory.SetState(role, AiModelState.NotDownloaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download failed for {Role}", role);
                _inventory.SetState(role, AiModelState.Error);
            }
            finally
            {
                lock (_lock)
                {
                    _activeDownloads.Remove(role);
                    _progress.Remove(role);
                }
            }
        }, CancellationToken.None);
    }

    private async Task DownloadCoreAsync(AiModelRole role, AiModelDefinition definition, CancellationToken ct)
    {
        var modelPath = _inventory.GetModelPath(role);
        var directory = Path.GetDirectoryName(modelPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = modelPath + ".downloading";

        var client = _httpClientFactory.CreateClient("ModelDownload");
        using var response = await client.GetAsync(definition.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? (definition.SizeMB * 1024L * 1024L);
        lock (_lock) { _progress[role] = (0, totalBytes); }

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long downloaded = 0;
        int bytesRead;
        var lastProgressReport = DateTimeOffset.MinValue;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloaded += bytesRead;

            lock (_lock) { _progress[role] = (downloaded, totalBytes); }

            // Report progress at most every 500ms to avoid flooding SignalR.
            if (DateTimeOffset.UtcNow - lastProgressReport > TimeSpan.FromMilliseconds(500))
            {
                lastProgressReport = DateTimeOffset.UtcNow;
                var percent = totalBytes > 0 ? (int)(downloaded * 100 / totalBytes) : 0;
                await _eventPublisher.PublishAsync(SignalREvents.ModelDownloadProgress, new
                {
                    Role = role.ToString(),
                    Percent = percent,
                    BytesDownloaded = downloaded,
                    TotalBytes = totalBytes,
                }, ct);
            }
        }

        // Validate SHA-256 if configured.
        if (!string.IsNullOrEmpty(definition.Sha256))
        {
            _logger.LogInformation("Validating SHA-256 for {Role}...", role);
            fileStream.Position = 0;
            var hash = await SHA256.HashDataAsync(fileStream, ct);
            var hashHex = Convert.ToHexStringLower(hash);

            if (!hashHex.Equals(definition.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                throw new InvalidOperationException(
                    $"SHA-256 mismatch for {role}: expected {definition.Sha256}, got {hashHex}");
            }
        }

        // Atomic rename.
        fileStream.Close();
        File.Move(tempPath, modelPath, overwrite: true);

        _inventory.SetState(role, AiModelState.Ready);
        _logger.LogInformation("Model {Role} downloaded successfully: {Path} ({MB} MB)", role, modelPath, downloaded / (1024 * 1024));

        await _eventPublisher.PublishAsync(SignalREvents.ModelStateChanged, new
        {
            Role = role.ToString(),
            OldState = AiModelState.Downloading.ToString(),
            NewState = AiModelState.Ready.ToString(),
        }, ct);
    }

    public async Task CancelDownloadAsync(AiModelRole role, CancellationToken ct = default)
    {
        CancellationTokenSource? cts;
        lock (_lock) { _activeDownloads.TryGetValue(role, out cts); }
        if (cts is not null)
        {
            await cts.CancelAsync();
        }
    }

    public AiModelStatus GetStatus(AiModelRole role)
    {
        var def = _inventory.GetDefinition(role);
        long downloaded = 0, total = 0;
        lock (_lock)
        {
            if (_progress.TryGetValue(role, out var p))
            {
                downloaded = p.Downloaded;
                total = p.Total;
            }
        }

        return new AiModelStatus
        {
            Role = role,
            ModelType = role == AiModelRole.Audio ? AiModelType.Audio : AiModelType.Text,
            State = _inventory.GetState(role),
            ModelFile = def.File,
            SizeMB = def.SizeMB,
            DownloadProgressPercent = total > 0 ? (int)(downloaded * 100 / total) : 0,
            BytesDownloaded = downloaded,
            TotalBytes = total,
        };
    }

    public IReadOnlyList<AiModelStatus> GetAllStatuses() =>
        Enum.GetValues<AiModelRole>().Select(GetStatus).ToList();

    public bool AreAllModelsReady() => _inventory.AreAllReady();

    public Task DeleteModelAsync(AiModelRole role, CancellationToken ct = default)
    {
        var path = _inventory.GetModelPath(role);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted model file for {Role}: {Path}", role, path);
        }
        _inventory.SetState(role, AiModelState.NotDownloaded);
        return Task.CompletedTask;
    }
}
