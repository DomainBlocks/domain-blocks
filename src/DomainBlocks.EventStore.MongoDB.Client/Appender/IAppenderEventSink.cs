using DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

// Implementation is decision-maker / state machine
public interface IAppenderEventSink
{
    // Invoke AckRequests to acknowledge all locally completed requests. Have a sensible TTL on Succeeded/Rejected
    // status so completed requests are self-purging.
    // Client may attempt to re-request when already Succeeded/Rejected. Detect and immediately complete directly.
    // Client may attempt to re-request after TTL and request purged. Let it flow through the pipeline and have
    // idempotency mark it as succeeded. Might be hard to detect this.
    // void OnStarted();
    //
    // void OnLeadershipAcquired();
    //
    // void OnLeadershipLost();
    //
    // void OnLeaderCaughtUp();
    //
    // void OnRequestSubmitted();
    //
    // void OnRequestOutcomeRecorded();
    //
    // void OnCommitBatchRecorded();
    //
    // void OnCommitPositionAdvanced();

    ValueTask OnEventAsync(AppenderEventEnvelope envelope, CancellationToken cancellationToken = default);
}