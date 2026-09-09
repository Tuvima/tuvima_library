using System.Threading.Channels;

namespace MediaEngine.Api.Services.View;

public interface IViewSharedContributionQueue
{
    ValueTask EnqueueAsync(Guid contributionId, CancellationToken ct = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct = default);
}

public sealed class ViewSharedContributionQueue : IViewSharedContributionQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public ValueTask EnqueueAsync(Guid contributionId, CancellationToken ct = default) =>
        _queue.Writer.WriteAsync(contributionId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct = default) =>
        _queue.Reader.ReadAllAsync(ct);
}
