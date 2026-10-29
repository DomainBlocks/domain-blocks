using DomainBlocks.EventStore.PostgreSQL.Feeds;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit.Feeds;

/// <summary>
/// A session factory that opens sessions with a delegate and holds nothing across them. It treats an
/// <see cref="IOException"/> as transient, unless the test says otherwise.
/// </summary>
internal sealed class DelegateSessionFactory<T>(
    Func<CancellationToken, Task<IEventLogSession<T>>> open,
    Func<Exception, bool>? isTransient = null) :
    IEventLogSessionFactory<T>
{
    private readonly Func<Exception, bool> _isTransient = isTransient ?? (static ex => ex is IOException);

    public int DisposeCount { get; private set; }

    public Task<IEventLogSession<T>> OpenAsync(CancellationToken cancellationToken) => open(cancellationToken);

    public bool IsTransient(Exception exception) => _isTransient(exception);

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}