using Microsoft.Extensions.Options;

namespace MediaEngine.Ingestion.Tests.Helpers;

internal sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
{
    public OptionsMonitorStub(T currentValue)
        => CurrentValue = currentValue;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
