using System.Threading.Channels;

namespace MediTrail.Api.AiPipeline;

/// <summary>
/// Work item for the background worker. Only ids cross the boundary — the worker resolves its own
/// scoped DbContext, so nothing entity-tracked leaks across scopes.
/// </summary>
public readonly record struct ProcessingJob(Guid PatientId, Guid DocumentId);

/// <summary>
/// An in-process channel, deliberately chosen over an external broker (§14.2): durability comes from
/// <c>documents.status</c> in Postgres, not from the queue, so a restart re-enqueues rather than
/// losing work. Behind an interface so a real broker can replace it if volume ever justifies one.
/// </summary>
public interface IProcessingQueue
{
    ValueTask EnqueueAsync(ProcessingJob job, CancellationToken ct = default);
    IAsyncEnumerable<ProcessingJob> DequeueAllAsync(CancellationToken ct);
}

public sealed class ChannelProcessingQueue : IProcessingQueue
{
    // Unbounded: the real backpressure is the worker's concurrency limit and the AI provider's
    // rate limit, and dropping an accepted upload would be worse than queueing it.
    private readonly Channel<ProcessingJob> _channel =
        Channel.CreateUnbounded<ProcessingJob>(new UnboundedChannelOptions { SingleReader = false });

    public ValueTask EnqueueAsync(ProcessingJob job, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(job, ct);

    public IAsyncEnumerable<ProcessingJob> DequeueAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
