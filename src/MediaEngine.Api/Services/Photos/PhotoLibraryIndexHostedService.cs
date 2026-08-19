namespace MediaEngine.Api.Services.Photos;

/// <summary>Refreshes configured photo libraries without using the catalogue ingestion queue.</summary>
public sealed class PhotoLibraryIndexHostedService(
    PhotoLibraryService photos,
    ILogger<PhotoLibraryIndexHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await photos.ScanAsync(stoppingToken);
                    if (result.FilesSeen > 0 || result.Errors > 0)
                    {
                        logger.LogInformation(
                            "Photo index refreshed: {Seen} seen, {Added} photos added, {Duplicates} duplicates, {Errors} errors",
                            result.FilesSeen, result.PhotosAdded, result.DuplicatesFound, result.Errors);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Scheduled photo index refresh failed");
                }

                await Task.Delay(RefreshInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
