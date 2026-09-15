# MongoDB catch-up subscription: correctness review

> **Generated note.** Written by Claude Code (Claude Fable 5.1) on 15 September 2026 at the request of the author,
> after the read concern, read preference and change stream anchoring work on the MongoDB store. Where it disagrees with
> the code, the code is right.

## Verdict

The mechanism is correct for loss and duplication under the conditions it targets: concurrent appenders, subscribers
sharing one change stream, overflow recovery, network resumes, and a primary stepdown. No way to lose or duplicate an
event was found. Two liveness gaps, one resource leak and two minor edges remain, none introduced by the anchoring work.

## Why it is correct

The argument rests on one property of the store: positions are committed in optime order. Two appenders serialise on
the counter document inside their transactions, so a later position always commits later, and one commit's events
share one optime. Every majority snapshot is therefore a prefix of the position order.

- **Fresh connection.** The anchor is the primary's last applied optime before the cursor opens. The stream starts one
  tick after it, so it carries everything after the anchor. The high-water mark read waits until the commit point
  passes the anchor, so its snapshot is a prefix containing everything up to the anchor and possibly more. *H* is the
  end of that prefix. Live events at or below *H* were in the prefix and are dropped; events above *H* have optimes
  above the snapshot and arrive on the stream.
- **Later subscriber on a shared connection.** Its anchor is the connection's, already satisfied, so the read returns
  the current prefix. Any event notified before it attached was majority-committed before its read and is in the
  prefix. Any event majority-committed after its read is above *H* and is pushed to it, since it is attached. The
  overlap in between is filtered by *H*.
- **Catch-up query.** Same causal session as the mark, so its first snapshot contains *H*, and later batches can only
  add rows above *H*, which the filter excludes. The log is append-only, so nothing below *H* changes.
- **Overflow.** `FellBehind` is yielded before re-attaching, the resume origin is the last delivered position, and the
  new cycle repeats the argument from there. The exclusive start keeps the last commit from being replayed into the
  new queue.
- **Resume.** The token is seeded at open and updated per batch, so a resumed cursor continues where the last one
  stopped and the anchor stays valid. Rolled-back writes were never emitted and never read, and their reissued
  positions carry new optimes above the anchor.
- **Stepdown between `hello` and the open.** The new primary's clock is at or above the old anchor through gossip, so
  its new writes start above the anchor, and the old anchor's prefix is majority-committed on it by election.

## Findings

1. **Overflow during catch-up can stall under sustained writes.** `catchUpCts` is linked to the overflow token, so an
   overflow cancels the catch-up query. With a small queue and a steady write rate, each cycle can overflow before the
   catch-up yields anything, and the resume origin never advances. Catch-up results are valid regardless of the live
   queue, so let the catch-up run to *H*, then report `FellBehind` when the live drain finds the channel completed.
   That guarantees progress to *H* every cycle. `SubscriptionAsyncEnumerable.ReadAllAsync`.
2. **Cancelling `ConnectAsync` leaks the connection.** It constructs the `Connection`, whose producer task starts
   immediately, then awaits with the token. If the wait is cancelled the connection is dropped without disposal, and
   the cursor and producer keep running. Dispose it on that path. `ChangeStreamSubject.ConnectAsync`.
3. **Non-resumable stream errors fault the subscription.** A `ChangeStreamHistoryLost` after a long outage, for
   example, reaches the consumer as an exception rather than `FellBehind`, and the consumer has to resubscribe from its
   last position. This matches the contract note's "no reset path" and is consistent, but the PostgreSQL store recovers
   in-process, and MongoDB could do the same by treating a faulted connection like an overflow.
4. **A catch-up cursor that dies mid-read faults the subscription.** The driver retries the initial find once, but not
   a `getMore`. A stepdown during a long catch-up surfaces as an error. Same shape as finding 3.
5. **Benign race on detach.** The producer iterates an immutable snapshot of attachments, so it can call a
   just-detached observer once after it is disposed. `TryWrite` then lands in a channel nobody reads, or `Cancel` on
   the disposed source throws and is logged as an observer failure. No effect on correctness, only log noise.

Findings 1 and 2 are small, contained changes. Findings 3 and 4 are a design choice about who owns recovery, worth a
decision rather than a quick fix.