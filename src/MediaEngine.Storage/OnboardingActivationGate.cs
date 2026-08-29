namespace MediaEngine.Storage;

/// <summary>Prevents library and AI background work from running before first-run setup is accepted.</summary>
public sealed class OnboardingActivationGate(OnboardingRepository repository)
{
    public bool IsComplete => string.Equals(repository.Get().State, "complete", StringComparison.Ordinal);

    public async Task WaitAsync(CancellationToken ct)
    {
        while (!IsComplete)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }
}
