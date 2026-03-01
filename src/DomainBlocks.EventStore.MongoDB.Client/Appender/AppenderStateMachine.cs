using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Events;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Scheduling;
using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

public sealed class AppenderStateMachine : IAppenderEventSink
{
    private readonly ILeaderWorkScheduler _leaderWorkScheduler = null!;
    private LeaseClaim? _lastAcquiredLeaseClaim;
    private ILeaseHandle<LeaseState>? _leaseHandle;

    [MemberNotNullWhen(true, nameof(_lastAcquiredLeaseClaim))]
    [MemberNotNullWhen(true, nameof(_leaseHandle))]
    private bool IsLeader => _lastAcquiredLeaseClaim is not null &&
                             _leaseHandle is not null &&
                             _lastAcquiredLeaseClaim == _leaseHandle.Claim;

    public ValueTask OnEventAsync(AppenderEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var task = envelope.Event switch
        {
            AppendRequestObserved => ValueTask.CompletedTask,
            AppendRequestRetried => ValueTask.CompletedTask,
            CommitOutcomeObserved => ValueTask.CompletedTask,
            LeaderLeaseAcquired e => OnLeaderLeaseAcquiredAsync(e, cancellationToken),
            LeaderLeaseLost e => OnLeaderLeaseLostAsync(e, cancellationToken),
            LeaseUpdateObserved e => OnLeaseUpdateObservedAsync(e, cancellationToken),
            _ => ValueTask.CompletedTask
        };

        return task;
    }

    private ValueTask OnLeaderLeaseAcquiredAsync(LeaderLeaseAcquired @event, CancellationToken cancellationToken)
    {
        _leaseHandle = @event.Handle;
        return ScheduleStepUpIfLeaderAsync(cancellationToken);
    }

    private ValueTask OnLeaderLeaseLostAsync(LeaderLeaseLost @event, CancellationToken cancellationToken)
    {
        _leaseHandle = null;
        return ValueTask.CompletedTask;
    }

    private ValueTask OnLeaseUpdateObservedAsync(LeaseUpdateObserved @event, CancellationToken cancellationToken)
    {
        if (@event.Snapshot.LastUpdateKind is LeaseUpdateKind.Acquired or LeaseUpdateKind.Renewed)
        {
            _lastAcquiredLeaseClaim = @event.Snapshot.Claim;
            return ScheduleStepUpIfLeaderAsync(cancellationToken);
        }

        if (@event.Snapshot.LastUpdateKind is LeaseUpdateKind.Released)
            _lastAcquiredLeaseClaim = null;

        return ValueTask.CompletedTask;
    }

    private ValueTask ScheduleStepUpIfLeaderAsync(CancellationToken cancellationToken)
    {
        return IsLeader
            ? _leaderWorkScheduler.ScheduleStepUpAsync(_leaseHandle, cancellationToken)
            : ValueTask.CompletedTask;
    }
}