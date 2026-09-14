# What an event store needs from its database

> **Generated note.** Written by Claude Code (Claude Fable 5.1) on 14 September 2026 at the request of the author,
> based on the author's observation that the MongoDB and PostgreSQL implementations of `IEventStore` had converged on
> the same shape. Where it disagrees with the code, the code is right.

The MongoDB and PostgreSQL implementations of `IEventStore` were built independently, yet they converged on the same
shape. This note distils that shape into a contract: the guarantees a database must offer, and how the store builds a
correct catch-up-then-live subscription on top of them. It is the cross-store behaviour that the contract tests in
`tests/DomainBlocks.Testing.Integration.EventStore/Contract` exercise.

## The idea in one sentence

An event store is correct if the database gives it one total order over all events that is both **sortable after the
fact** and **streamable as it happens**, so that a historical query and a live feed can be stitched together at a single
point without loss or duplication.

## Three guarantees

### 1. A commit-ordered position

Every event carries a key, the *log position*, with two properties:

- It is strictly increasing across all appenders, whatever their concurrency.
- No event becomes visible to readers before every event with a lower position is visible.

The second property is the important one, and it is stronger than "monotonic". It means that a reader who has seen
position *p* and asks for everything above *p* can never miss an event that is later committed with a position below
*p*. Sorting by position therefore yields a stable, replayable log, and `WHERE position > @last ORDER BY position` is an
exact, keyset-paginated tail.

Native keys do not provide this. `bigserial`, `ObjectId`, transaction ids and cluster time are all assigned *before*
commit, so under concurrency a lower key can become visible after a higher one and a tailer skips it. Both stores
therefore construct the position themselves, and only ask the database for two primitives:

- a mutual-exclusion primitive that is held until commit, and
- an atomic multi-row write, so a batch appears all at once or not at all.

PostgreSQL provides the mutex as a `FOR UPDATE` lock on a single counter row inside the append function's transaction.
MongoDB provides it as a single sequenced writer in the `DomainBlocks.MongoDB.Sequencing` package.

Gap-freeness is a consequence of how the position is constructed, not a requirement of the algorithm. The subscription
logic below relies only on commit order.

### 2. A push feed of committed changes, in that same order

The database must be able to stream inserts to a client such that:

- only committed data is emitted,
- events arrive in position order, and
- the feed is resumable from a cursor for the lifetime of a session.

PostgreSQL logical replication (`pgoutput`, streaming off, so only whole committed transactions are sent in commit
order) and MongoDB change streams (inserts only, resume token) both qualify.

The feed's own cursor (a WAL location, a resume token) never becomes the subscriber's checkpoint. The log position is
the checkpoint: it is durable, meaningful to the historical query, and independent of which feed session delivered the
event. The feed cursor only has to keep one session alive.

### 3. Historical reads at least as current as the feed

A historical query issued after an observer is attached to the feed must see every event that was committed before the
observer was attached. This is what makes the two halves join without a hole: an event committed before attachment is
not on the feed, so it has to be in the query.

The guarantee is trivially met when the query and the feed are served by the same node. The PostgreSQL store uses one
data source for both, and the MongoDB store relies on the client's default read preference of primary, which the
connection string can override. It fails silently when the query is served by a lagging replica. An event committed before attachment but not yet
replicated is in neither the query nor the feed, and is lost. MongoDB secondary reads and PostgreSQL streaming replicas
are exactly this trap, and an implementation that reads from a replica must route the catch-up query to the primary or
prove the replica has caught up to the feed's start point.

## Why the three are sufficient

The subscription algorithm, identical in both stores:

1. Attach an observer to the live feed **first**.
2. Read the *high-water mark* *H*, the highest position currently in the log.
3. Replay the historical log from the last delivered position up to and including *H*.
4. Emit `CaughtUp`.
5. Drain live events, discarding any with position at or below *H*.

Guarantee 1 makes step 3 exact: every event at or below *H* is visible, and none is missing. Guarantee 2 means every
event above *H* arrives on the feed, in order, because the observer was attached before *H* was read. Guarantee 3 means
nothing falls between the two halves. The filter in step 5 removes the overlap, so there are no duplicates.

Reading *H* explicitly is an implementation choice, not something the database has to provide beyond guarantee 1. The
replay could instead page forward until it finds nothing more, or until it reaches a position the feed has already
delivered, and use the last position it observed as *H*. That is equally correct. The explicit read buys a bounded
catch-up with a definite place to emit `CaughtUp`, where "page until empty" needs a termination rule and can chase the
tail under sustained write load. If the bound turns out to be too far away, the bounded live queue and the recovery path
below take care of it.

## Recovery collapses into one path

Because the position is the checkpoint, the subscriber never needs to know *what* it missed, only *that* it may have
missed something. Two situations produce that signal:

- The subscriber's bounded queue overflowed.
- The feed session was lost and re-established, and the new session cannot promise continuity.

Both are reported as `FellBehind`, and both are handled the same way: run the algorithm again from the last delivered
position. Anything committed during the gap has a position at or below the new high-water mark and is picked up by the
replay. Anything after it is delivered by the new feed session.

The one ordering rule that makes this safe is that a reset must be signalled **before** any event from the new feed
session is delivered. Otherwise a new-session event could advance the subscriber's position past the gap.

## Supporting guarantees for writes

Two further properties concern the correctness of appends rather than reads:

- **Unique constraints** give per-stream optimistic concurrency (unique on `streamId, streamPosition`) and idempotent
  retries (unique on the commit id).
- **Atomic multi-row commit** makes a batch of events appear all at once, so a reader never observes half a commit.

## How the two stores map onto the contract

| Guarantee               | PostgreSQL                                              | MongoDB                                      |
|-------------------------|---------------------------------------------------------|----------------------------------------------|
| Commit-ordered position | `FOR UPDATE` on one sequence row, held to commit        | Sequencing package, single sequenced writer  |
| Ordered committed feed  | `pgoutput`, temporary slot, streaming off               | Change stream, inserts only, resume token    |
| Reads current with feed | Same data source for query and feed                     | Default read preference, primary; not pinned |
| Feed continuity         | Not guaranteed across reconnect; reset triggers restart | Resume token covers reconnect; no reset path |
| Idempotency             | Unique partial index on commit id                       | Pre-commit probe on commit id                |

## What the abstraction requires

`LogPosition` is documented with exactly this property: values increase in commit order, and no event is visible at a
position until every event at a lower position is visible. Gaps are permitted, because they are harmless to the
algorithm above.

A store that offers a native position with this property (a server-assigned commit position, say) can use it directly.
A store that does not must construct one, as both current implementations do.
