namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Represents what a run did, and the event log that it left once its faults were healed and its writer had stopped.
/// </summary>
/// <param name="operations">The writer's operations, in order.</param>
/// <param name="log">Every event in the event log, in log order.</param>
/// <param name="received">Every event that the subscriber received, in order.</param>
/// <param name="subscriptionError">
/// The error that ended the subscription, or <see langword="null"/> if none did.
/// </param>
/// <param name="fellBehindCount">The number of times that the subscription fell behind.</param>
internal sealed class RunRecord(
    IReadOnlyList<AppendOperation> operations,
    IReadOnlyList<ObservedEvent> log,
    IReadOnlyList<ObservedEvent> received,
    Exception? subscriptionError,
    int fellBehindCount)
{
    public IReadOnlyList<AppendOperation> Operations { get; } = operations;

    public IReadOnlyList<ObservedEvent> Log { get; } = log;

    public IReadOnlyList<ObservedEvent> Received { get; } = received;

    public Exception? SubscriptionError { get; } = subscriptionError;

    public int FellBehindCount { get; } = fellBehindCount;
}