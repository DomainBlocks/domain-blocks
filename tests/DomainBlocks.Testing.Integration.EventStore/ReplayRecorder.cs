using System.Runtime.CompilerServices;
using DomainBlocks.EventStore;

namespace DomainBlocks.Testing.Integration.EventStore;

/// <summary>
/// Records each replay that a subscription asks its catch-up reader for, and can hold one back part-way.
/// </summary>
public sealed class ReplayRecorder
{
    private readonly List<Replay> _replays = [];
    private int _heldReplayIndex = -1;
    private TaskCompletionSource? _releaseHeldReplay;

    public IReadOnlyList<Replay> Replays => _replays;

    /// <summary>
    /// Holds the replay with the given index once it has delivered its first event, until the hold is released.
    /// </summary>
    public Hold HoldReplay(int replayIndex)
    {
        _heldReplayIndex = replayIndex;
        _releaseHeldReplay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return new Hold(_releaseHeldReplay);
    }

    /// <summary>
    /// Passes a replay through, noting what was asked for and whether it was read to its end.
    /// </summary>
    public async IAsyncEnumerable<ReadEvent<object, string, TStreamPos, TLogPos>> Record<TStreamPos, TLogPos>(
        IAsyncEnumerable<ReadEvent<object, string, TStreamPos, TLogPos>> events,
        long afterExclusive,
        long highWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TStreamPos : notnull
        where TLogPos : notnull
    {
        var index = _replays.Count;
        _replays.Add(new Replay(afterExclusive, highWaterMark, Completed: false));

        var isFirstEvent = true;

        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            yield return e;

            if (isFirstEvent && index == _heldReplayIndex && _releaseHeldReplay is { } release)
                await release.Task.WaitAsync(cancellationToken);

            isFirstEvent = false;
        }

        _replays[index] = _replays[index] with { Completed = true };
    }

    public sealed record Replay(long AfterExclusive, long HighWaterMark, bool Completed);

    public sealed class Hold(TaskCompletionSource release)
    {
        public void Release() => release.SetResult();
    }
}