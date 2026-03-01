using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Scheduling;

public interface ILeaderWorkScheduler
{
    // - Play through log from checkpoint position to HW mark
    // - For normal events with commit index zero, consider that as "commit succeeded"
    // - Or, alternatively, we could record a CommitAccepted event
    // - CommitRejected signals that a given commit is permanently rejected, i.e. OCC failed for expected state
    // - Mark request document with appropriate status, e.g. Pending -> Committed/Rejected
    // Scenario 1: Leader crashes before HW mark advances - requests are retried by new leader, maybe overwriting slots
    // Scenario 2: Leader crashes after HW mark but before requests are marked as completed - next leader runs this
    // catch-up phase to ensure relevant requests are marked as completed.
    // This implies commitId cannot be unique in the dbx_logged_events collection, as we need slots above HW mark to be
    // overwritable. So, completed request status is idempotency guard. Downside: inbox retention is needed forever.
    // Once done:
    // - Schedule pending requests for fulfilment from HW+1. Bug if HW+2 or more - advancement never happens (not
    //   contiguous). Bug if HW+0 or less - dangerous because greater epoch and overwrite. Must be careful here.
    // Given a lease and a HW mark, we know the following:
    // - If a stale leader exists, its epoch is lower
    // - That stale leader can still write to slots above its last known HW mark, which will be <= to our HW mark
    // - That stale leader cannot replace its own appends
    // - That stale leader cannot advance the HW mark
    // - That stale leader cannot replace our writes (our epoch is higher)
    // - We can write over any lower epoch slot above our known HW mark
    ValueTask ScheduleStepUpAsync(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken = default);

    // E=42, HW=10 (expected on lease acq. observation)
    // E=43, HW=20 (actual - I never got the chance to write)
    // What are the implications?
    // I start catching up on pending requests, attempting to write into pos >= 11.
    // I know I can't overwrite my own slots.
    // I can't overwrite because 42 < 43.

    // Include epoch and HW mark. Listener should have this information.
    // Or, where does the HW mark come from when fulfilling one or more requests? Via a read on lease state?
    // We might need to be append-only within an epoch. Fix partial failures in-place - retry or abort with filler
    // records. Base nextPos in memory from HW mark known at leader acquisition.
    // This allows safe HW advancement in the background. Better guarantees. We can't overwrite what we've already
    // written.
    // HW-mark background advancer halts if there is a gap (perhaps due to bug). We can detect with timeout and step
    // down, e.g. via a watchdog.
    void FulfilRequest();
}