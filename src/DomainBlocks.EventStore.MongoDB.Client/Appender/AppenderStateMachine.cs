using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Events;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Scheduling;
using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

public sealed class AppenderStateMachine : IAppenderEventSink
{
    private readonly INodeWorkScheduler _nodeWorkScheduler = null!;
    private readonly ILeaderWorkScheduler _leaderWorkScheduler = null!;
    private readonly LeaderTracker _leaderTracker;

    public AppenderStateMachine()
    {
        _leaderTracker = new LeaderTracker(OnLeadershipAcquiredAsync, OnLeadershipLostAsync);
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    // Guarantee that we observe no events until InitializeAsync completes (?)
    public ValueTask OnEventAsync(AppenderEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var task = envelope.Event switch
        {
            AppendRequestObserved => ValueTask.CompletedTask,
            AppendRequestRetried => ValueTask.CompletedTask,
            CommitOutcomeObserved => ValueTask.CompletedTask,
            LocalLeaseAcquired e => OnLocalLeaseAcquiredAsync(e, cancellationToken),
            LocalLeaseLost e => OnLocalLeaseLostAsync(e, cancellationToken),
            LeaseUpdateObserved e => OnLeaseUpdateObservedAsync(e, cancellationToken),
            _ => ValueTask.CompletedTask
        };

        return task;
    }

    private ValueTask OnLocalLeaseAcquiredAsync(LocalLeaseAcquired @event, CancellationToken cancellationToken)
    {
        return _leaderTracker.SetHandleAsync(@event.Handle, cancellationToken);
    }

    private ValueTask OnLocalLeaseLostAsync(LocalLeaseLost @event, CancellationToken cancellationToken)
    {
        return _leaderTracker.ClearHandleAsync(@event.Claim, cancellationToken);
    }

    private ValueTask OnLeaseUpdateObservedAsync(LeaseUpdateObserved @event, CancellationToken cancellationToken)
    {
        var claim = @event.Snapshot.Claim;

        return @event.Snapshot.LastUpdateKind switch
        {
            LeaseUpdateKind.Acquired => _leaderTracker.SetObservedClaimAsync(claim, cancellationToken),
            LeaseUpdateKind.Renewed => _leaderTracker.SetObservedClaimAsync(claim, cancellationToken),
            LeaseUpdateKind.Released => _leaderTracker.ClearObservedClaimAsync(claim, cancellationToken),
            _ => ValueTask.CompletedTask
        };
    }

    private ValueTask OnLeadershipAcquiredAsync(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken)
    {
        return _leaderWorkScheduler.ScheduleStepUpAsync(handle, cancellationToken);
    }

    private ValueTask OnLeadershipLostAsync(CancellationToken cancellationToken)
    {
        return _leaderWorkScheduler.ScheduleStepDownAsync(cancellationToken);
    }

    private sealed class LeaderTracker(
        Func<ILeaseHandle<LeaseState>, CancellationToken, ValueTask> onLeadershipAcquired,
        Func<CancellationToken, ValueTask> onLeadershipLost)
    {
        private LeaseClaim? _observedClaim;
        private ILeaseHandle<LeaseState>? _handle;
        private bool _prevIsLeader;

        [MemberNotNullWhen(true, nameof(_observedClaim))]
        [MemberNotNullWhen(true, nameof(_handle))]
        private bool IsLeader => _observedClaim is not null && _handle is not null && _observedClaim == _handle.Claim;

        public ValueTask SetObservedClaimAsync(LeaseClaim observedClaim, CancellationToken cancellationToken)
        {
            _observedClaim = observedClaim;
            return EvaluateAsync(cancellationToken);
        }

        public ValueTask SetHandleAsync(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken)
        {
            _handle = handle;
            return EvaluateAsync(cancellationToken);
        }

        public ValueTask ClearObservedClaimAsync(LeaseClaim observedClaim, CancellationToken cancellationToken)
        {
            if (observedClaim != _observedClaim)
                return ValueTask.CompletedTask;

            _observedClaim = null;
            return EvaluateAsync(cancellationToken);
        }

        public ValueTask ClearHandleAsync(LeaseClaim claim, CancellationToken cancellationToken)
        {
            if (claim != _handle?.Claim)
                return ValueTask.CompletedTask;

            _handle = null;
            return EvaluateAsync(cancellationToken);
        }

        private ValueTask EvaluateAsync(CancellationToken cancellationToken)
        {
            if (IsLeader == _prevIsLeader)
                return ValueTask.CompletedTask;

            _prevIsLeader = IsLeader;
            return IsLeader ? onLeadershipAcquired(_handle, cancellationToken) : onLeadershipLost(cancellationToken);
        }
    }
}