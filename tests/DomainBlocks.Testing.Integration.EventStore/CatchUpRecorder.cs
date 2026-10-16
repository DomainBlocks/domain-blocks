using System.Runtime.CompilerServices;
using DomainBlocks.EventStore;

namespace DomainBlocks.Testing.Integration.EventStore;

/// <summary>
/// Records each catch-up that a subscription asks for. Allows blocking a catch-up operation partway through.
/// </summary>
public sealed class CatchUpRecorder
{
    private readonly List<CatchUpRead> _reads = [];
    private int _holdIndex = -1;
    private TaskCompletionSource? _holdTcs;

    /// <summary>
    /// Gets the recorded catch-up reads.
    /// </summary>
    public IReadOnlyList<CatchUpRead> Reads => _reads;

    /// <summary>
    /// Holds the replay with the given index once it has delivered its first event, until the hold is released.
    /// </summary>
    public CatchUpHold Hold(int index)
    {
        _holdIndex = index;
        _holdTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return new CatchUpHold(_holdTcs);
    }

    /// <summary>
    /// Records a catch-up and passes it through.
    /// </summary>
    public async IAsyncEnumerable<ReadEvent<object, string, TStreamPos, TLogPos>> Record<TStreamPos, TLogPos>(
        IAsyncEnumerable<ReadEvent<object, string, TStreamPos, TLogPos>> events,
        long startExclusive,
        long endInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        var index = _reads.Count;
        _reads.Add(new CatchUpRead(startExclusive, endInclusive, IsCompleted: false));

        var isFirstEvent = true;

        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            yield return e;

            if (isFirstEvent && index == _holdIndex && _holdTcs is { } tcs)
                await tcs.Task.WaitAsync(cancellationToken);

            isFirstEvent = false;
        }

        _reads[index] = _reads[index] with { IsCompleted = true };
    }
}