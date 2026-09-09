using MediaEngine.Domain.Events;

namespace MediaEngine.Api.Services.Events;

public sealed class ApplicationEventDispatchWorker(
    IApplicationEventRepository repository,
    ApplicationEventDispatcher dispatcher,
    ILogger<ApplicationEventDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long? cursor = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                cursor ??= (await repository.GetBoundsAsync(stoppingToken).ConfigureAwait(false)).LatestSequence ?? 0;
                IReadOnlyList<StoredApplicationEvent> page;
                do
                {
                    page = await repository.ReadAfterAsync(cursor.Value, 250, stoppingToken).ConfigureAwait(false);
                    foreach (var value in page)
                    {
                        await dispatcher.DispatchAsync(value, stoppingToken).ConfigureAwait(false);
                        cursor = value.Sequence;
                    }
                } while (page.Count == 250);
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Application event dispatch polling failed; retrying.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
