using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Internal.Feeds;

namespace DomainBlocks.EventStore.Internal.Tests.Unit.Feeds;

/// <summary>
/// A fake session that plays a script of row positions, failures, and endings, then blocks like a live session would.
/// </summary>
internal sealed class ScriptedSession(string description, params ScriptedSession.Step[] steps) :
    IFeedSession<long, long>
{
    public abstract record Step
    {
        public sealed record Row(long Position) : Step;

        public sealed record Throw(Exception Exception) : Step;

        public sealed record End : Step;
    }

    private readonly Queue<Step> _steps = new(steps);
    private long _highWaterMark = -1;

    public string Description { get; } = description;

    public bool IsResumed { get; init; }

    /// <summary>
    /// Gets the position of the last row played, or the mark that the session starts with, which is -1 by default.
    /// </summary>
    public long HighWaterMark
    {
        get => _highWaterMark;
        init => _highWaterMark = value;
    }

    public int DisposeCount { get; private set; }

    public static Step.Row Row(long position) => new(position);

    public static Step.Throw Throw(Exception exception) => new(exception);

    public static Step.End End() => new();

    public async IAsyncEnumerable<long> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_steps.Count == 0)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            switch (_steps.Dequeue())
            {
                case Step.Row row:
                    _highWaterMark = row.Position;
                    yield return row.Position;
                    break;

                case Step.Throw failure:
                    throw failure.Exception;

                case Step.End:
                    yield break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}