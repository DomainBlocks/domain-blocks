using System.Diagnostics.CodeAnalysis;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Events;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Scheduling;
using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

public sealed class AppenderStateMachine : IAppenderEventSink
{
    private readonly INodeWorkScheduler _nodeWorkScheduler = null!;
    private readonly ILeaderWorkScheduler _leaderWorkScheduler = null!;
    private readonly LeaderTracker _leaderTracker = new();

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
            LeaderLeaseAcquired e => OnLeaderLeaseAcquiredAsync(e, cancellationToken),
            LeaderLeaseLost e => OnLeaderLeaseLostAsync(e, cancellationToken),
            LeaseUpdateObserved e => OnLeaseUpdateObservedAsync(e, cancellationToken),
            _ => ValueTask.CompletedTask
        };

        return task;
    }

    private ValueTask OnLeaderLeaseAcquiredAsync(LeaderLeaseAcquired @event, CancellationToken cancellationToken)
    {
        var transition = _leaderTracker.SetHandle(@event.Handle);
        return OnLeaderTransitionAsync(transition, cancellationToken);
    }

    private ValueTask OnLeaderLeaseLostAsync(LeaderLeaseLost @event, CancellationToken cancellationToken)
    {
        var transition = _leaderTracker.Clear();
        return OnLeaderTransitionAsync(transition, cancellationToken);
    }

    private ValueTask OnLeaseUpdateObservedAsync(LeaseUpdateObserved @event, CancellationToken cancellationToken)
    {
        if (@event.Snapshot.LastUpdateKind is LeaseUpdateKind.Acquired or LeaseUpdateKind.Renewed)
        {
            var transition = _leaderTracker.SetClaim(@event.Snapshot.Claim);
            return OnLeaderTransitionAsync(transition, cancellationToken);
        }

        if (@event.Snapshot.LastUpdateKind is LeaseUpdateKind.Released)
        {
            var transition = _leaderTracker.ClearClaim(@event.Snapshot.Claim);
            return OnLeaderTransitionAsync(transition, cancellationToken);
        }

        return ValueTask.CompletedTask;
    }

    private ValueTask OnLeaderTransitionAsync(LeaderTransition transition, CancellationToken cancellationToken)
    {
        return transition switch
        {
            LeaderTransition.LeadershipAcquired => _leaderWorkScheduler.ScheduleStepUpAsync(
                _leaderTracker.Handle,
                cancellationToken),

            LeaderTransition.LeadershipLost => _leaderWorkScheduler.ScheduleStepDownAsync(
                _leaderTracker.Handle,
                cancellationToken),

            _ => ValueTask.CompletedTask
        };
    }

    private sealed class LeaderTracker
    {
        private LeaseClaim? _claim;
        private ILeaseHandle<LeaseState>? _handle;
        private bool _prevIsLeader;

        public ILeaseHandle<LeaseState> Handle => IsLeader
            ? _handle
            : throw new InvalidOperationException("Lease handle is not available when not leader.");

        [MemberNotNullWhen(true, nameof(_claim))]
        [MemberNotNullWhen(true, nameof(_handle))]
        private bool IsLeader => _claim is not null && _handle is not null && _claim == _handle.Claim;

        public LeaderTransition SetClaim(LeaseClaim claim)
        {
            _claim = claim;
            return EvaluateTransition();
        }

        public LeaderTransition SetHandle(ILeaseHandle<LeaseState> handle)
        {
            _handle = handle;
            return EvaluateTransition();
        }

        public LeaderTransition Clear()
        {
            _claim = null;
            _handle = null;
            return EvaluateTransition();
        }

        public LeaderTransition ClearClaim(LeaseClaim claim)
        {
            return _claim == claim ? Clear() : LeaderTransition.None;
        }

        private LeaderTransition EvaluateTransition()
        {
            if (IsLeader == _prevIsLeader)
                return LeaderTransition.None;

            _prevIsLeader = IsLeader;
            return IsLeader ? LeaderTransition.LeadershipAcquired : LeaderTransition.LeadershipLost;
        }
    }

    private enum LeaderTransition
    {
        None,
        LeadershipAcquired,
        LeadershipLost
    }
}