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
            LocalLeaseLost => OnLocalLeaseLostAsync(cancellationToken),
            LeaseUpdateObserved e => OnLeaseUpdateObservedAsync(e, cancellationToken),
            _ => ValueTask.CompletedTask
        };

        return task;
    }

    private ValueTask OnLocalLeaseAcquiredAsync(LocalLeaseAcquired @event, CancellationToken cancellationToken)
    {
        return _leaderTracker.SetHandleAsync(@event.Handle, cancellationToken);
    }

    private ValueTask OnLocalLeaseLostAsync(CancellationToken cancellationToken)
    {
        return _leaderTracker.ClearAllAsync(cancellationToken);
    }

    private ValueTask OnLeaseUpdateObservedAsync(LeaseUpdateObserved @event, CancellationToken cancellationToken)
    {
        return @event.Snapshot.LastUpdateKind switch
        {
            LeaseUpdateKind.Acquired => _leaderTracker.SetClaimAsync(@event.Snapshot.Claim, cancellationToken),
            LeaseUpdateKind.Renewed => _leaderTracker.SetClaimAsync(@event.Snapshot.Claim, cancellationToken),
            LeaseUpdateKind.Released => _leaderTracker.ClearClaimAsync(@event.Snapshot.Claim, cancellationToken),
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
        private LeaseClaim? _claim;
        private ILeaseHandle<LeaseState>? _handle;
        private bool _prevIsLeader;

        [MemberNotNullWhen(true, nameof(_claim))]
        [MemberNotNullWhen(true, nameof(_handle))]
        private bool IsLeader => _claim is not null && _handle is not null && _claim == _handle.Claim;

        public ValueTask SetClaimAsync(LeaseClaim claim, CancellationToken cancellationToken)
        {
            _claim = claim;
            return EvaluateAsync(cancellationToken);
        }

        public ValueTask SetHandleAsync(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken)
        {
            _handle = handle;
            return EvaluateAsync(cancellationToken);
        }

        public ValueTask ClearClaimAsync(LeaseClaim claim, CancellationToken cancellationToken)
        {
            if (claim != _claim)
                return ValueTask.CompletedTask;

            _claim = null;
            return EvaluateAsync(cancellationToken);
        }

        public ValueTask ClearAllAsync(CancellationToken cancellationToken)
        {
            _claim = null;
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