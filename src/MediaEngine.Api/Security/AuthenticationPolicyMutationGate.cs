namespace MediaEngine.Api.Security;

public sealed class AuthenticationPolicyMutationGate
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
