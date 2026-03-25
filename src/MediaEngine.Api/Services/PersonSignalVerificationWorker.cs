namespace MediaEngine.Api.Services;

using MediaEngine.Domain.Contracts;

/// <summary>
/// Background worker that periodically checks for pending person signals
/// and triggers batch Wikidata verification. Runs every 5 minutes when
/// pending signals exist.
/// </summary>
public sealed class PersonSignalVerificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PersonSignalVerificationWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public PersonSignalVerificationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PersonSignalVerificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for startup to complete
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var pendingRepo = scope.ServiceProvider.GetRequiredService<IPendingPersonSignalRepository>();
                var count = await pendingRepo.GetCountAsync(stoppingToken);

                if (count > 0)
                {
                    _logger.LogInformation("PersonSignalVerificationWorker: {Count} pending signals, starting batch verification", count);
                    var verifier = scope.ServiceProvider.GetRequiredService<IPersonSignalVerificationService>();
                    var verified = await verifier.VerifyPendingSignalsAsync(stoppingToken);
                    _logger.LogInformation("PersonSignalVerificationWorker: verified {Verified} persons", verified);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "PersonSignalVerificationWorker: error during verification cycle");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
