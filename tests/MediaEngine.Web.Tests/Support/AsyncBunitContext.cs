using Bunit;

namespace MediaEngine.Web.Tests;

/// <summary>
/// Ensures MudBlazor and application services that implement only
/// <see cref="IAsyncDisposable"/> are released through bUnit's asynchronous path.
/// </summary>
public abstract class AsyncBunitContext : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();
}
