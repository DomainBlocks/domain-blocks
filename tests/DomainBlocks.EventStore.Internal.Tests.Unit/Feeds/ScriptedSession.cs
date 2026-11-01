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

        public sealed record Do(Action Action) : Step;
    }

    private readonly Queue<Step> _steps = new(steps);

    public string Description { get; } = description;

    public bool IsResumed { get; init; }

    /// <summary>
    /// Gets the mark that the session starts with, which is -1 by default.
    /// </summary>
    public long InitialMark { get; init; } = -1;

    public int DisposeCount { get; private set; }

    public static Step.Row Row(long position) => new(position);

    public static Step.Throw Throw(Exception exception) => new(exception);

    public static Step.End End() => new();

    public static Step.Do Do(Action action) => new(action);

    public async IAsyncEnumerable<(long Item, long Mark)> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_steps.Count == 0)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            switch (_steps.Dequeue())
            {
                case Step.Row row:
                    yield return (row.Position, row.Position);
                    break;

                case Step.Throw failure:
                    throw failure.Exception;

                case Step.End:
                    yield break;

                case Step.Do step:
                    step.Action();
                    break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}