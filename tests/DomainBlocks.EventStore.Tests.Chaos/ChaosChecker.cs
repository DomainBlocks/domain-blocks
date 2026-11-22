namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Checks what a run did against the event log that it left. The checks rely only on the identity that each event
/// carries, never on the store's internals.
/// </summary>
internal static class ChaosChecker
{
    public static ChaosReport Check(RunRecord run)
    {
        Violation[] violations =
        [
            .. FindLogGaps(run.Log),
            .. FindStreamGaps(run.Log),
            .. FindDuplicates(run.Log),
            .. FindLostAppends(run.Operations, run.Log),
            .. FindSubscriberDivergence(run.Log, run.Received),
            .. FindSubscriptionFailure(run.SubscriptionError)
        ];

        return new ChaosReport(run, violations);
    }

    private static IEnumerable<Violation> FindLogGaps(IReadOnlyList<ObservedEvent> log)
    {
        for (var i = 1; i < log.Count; i++)
        {
            if (log[i].LogPosition.Value != log[i - 1].LogPosition.Value + 1)
                yield return new Violation(Invariant.LogIsContiguous, $"The {log[i]} follows the {log[i - 1]}.");
        }
    }

    private static IEnumerable<Violation> FindStreamGaps(IReadOnlyList<ObservedEvent> log)
    {
        var lastByStream = new Dictionary<string, ObservedEvent>();

        foreach (var observed in log)
        {
            if (lastByStream.TryGetValue(observed.StreamId, out var previous) &&
                observed.StreamPosition.Value != previous.StreamPosition.Value + 1)
            {
                yield return new Violation(
                    Invariant.StreamsAreContiguous,
                    $"The {observed} follows the {previous} in its stream.");
            }

            lastByStream[observed.StreamId] = observed;
        }
    }

    private static IEnumerable<Violation> FindDuplicates(IReadOnlyList<ObservedEvent> log)
    {
        return log
            .GroupBy(x => (x.Operation, x.Index))
            .Where(x => x.Count() > 1)
            .Select(x => new Violation(
                Invariant.NoEventIsDuplicated,
                $"Event {x.Key.Operation}.{x.Key.Index} is at log positions " +
                $"{string.Join(", ", x.Select(y => y.LogPosition))}."));
    }

    private static IEnumerable<Violation> FindLostAppends(
        IReadOnlyList<AppendOperation> operations,
        IReadOnlyList<ObservedEvent> log)
    {
        var written = log.Select(x => (x.Operation, x.Index)).ToHashSet();

        foreach (var operation in operations.Where(x => x.Outcome == AppendOutcome.Ok))
        {
            var missingCount = Enumerable
                .Range(0, operation.EventCount)
                .Count(i => !written.Contains((operation.Number, i)));

            if (missingCount > 0)
            {
                yield return new Violation(
                    Invariant.NoAcknowledgedAppendIsLost,
                    $"Operation {operation.Number} returned, but {missingCount} of its {operation.EventCount} events " +
                    "are missing.");
            }
        }
    }

    // A skipped or repeated event shifts every later one, so only the first difference is reported.
    private static IEnumerable<Violation> FindSubscriberDivergence(
        IReadOnlyList<ObservedEvent> log,
        IReadOnlyList<ObservedEvent> received)
    {
        var sharedCount = Math.Min(log.Count, received.Count);

        for (var i = 0; i < sharedCount; i++)
        {
            if (received[i] != log[i])
            {
                yield return new Violation(
                    Invariant.SubscriberMatchesLog,
                    $"The subscriber's event {i} is the {received[i]}, but the log's is the {log[i]}.");

                yield break;
            }
        }

        if (received.Count > log.Count)
        {
            yield return new Violation(
                Invariant.SubscriberMatchesLog,
                $"The subscriber received {received.Count - log.Count} events after the log's last, starting with " +
                $"the {received[log.Count]}.");
        }
        else if (received.Count < log.Count)
        {
            yield return new Violation(
                Invariant.SubscriberCaughtUp,
                $"The subscriber received {received.Count} of the log's {log.Count} events, and not the " +
                $"{log[received.Count]}.");
        }
    }

    private static IEnumerable<Violation> FindSubscriptionFailure(Exception? error)
    {
        if (error is not null)
        {
            yield return new Violation(
                Invariant.SubscriptionKeptRunning,
                $"The subscription failed with {error.GetType().Name}: {error.Message}");
        }
    }
}