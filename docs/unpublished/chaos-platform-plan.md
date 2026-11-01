# Chaos platform for the event stores

> **Generated note.** Written by Claude (Claude Opus 5.5) on 29 October 2026 at the request of the author, from a
> brainstorm on the same day. It is a plan, not a record. Where it disagrees with the code, the code is right.

## Brief

Build a platform that runs workloads against the MongoDB and PostgreSQL event stores while injecting faults, and
proves afterwards that nothing went wrong:

- no lost or duplicated events, and strictly increasing log positions with no gaps
- writers retry with the same commit ID after an error or timeout, and the retry is idempotent
- subscribers resume on their own and deliver a contiguous stream
- subscribers that hit their retry limits give up as expected

Scenarios go from one database node, one writer and one subscriber, up to clusters with failover, many writers and
subscribers, and random network faults. Topologies are chosen by configuration: local containers, Patroni, Aurora and
Atlas. Runs start from the command line and can be watched, and a UI shows what is happening and lets a user inject
faults by hand. The UI matters early, because the platform is also for showing clients.

## Shape

```
Orchestrator (CLI and UI host)
 ├─ Topology       mongo-single | mongo-rs3 | pg-single | pg-sync-standby | pg-patroni3 | aurora | atlas
 ├─ Faults         IFault { InjectAsync, HealAsync }
 ├─ Nemesis        scripted timeline | seeded random | interactive
 ├─ Workload       writers and subscribers (in process first, separate processes later)
 ├─ History        run events, written to JSONL and published live
 ├─ Checker        live (provisional) and final (against the ground-truth log)
 └─ UI             Blazor Server, fed by the same run events
```

Everything that happens in a run is a run event: an append invoked or completed, an event received, a fault injected
or healed. The CLI, the UI, the report and replay are all views of that one record. The record is kept off the
database under test, so it survives the faults.

## Layout

A new top-level `chaos/` folder, added to `DomainBlocks.slnx`:

| Project                              | Contents                                                        |
|--------------------------------------|-----------------------------------------------------------------|
| `DomainBlocks.Chaos`                 | run events, history, checker, workload, faults, topologies      |
| `DomainBlocks.Chaos.Host`            | the `dbx-chaos` executable: CLI commands and the Blazor UI      |
| `DomainBlocks.Chaos.Tests.Unit`      | checker tests over synthetic histories                          |

Topologies reuse `MongoReplicaSet`, `PostgresServer`, `ReplicatedPostgresServer`, `TcpProxy` and `AuroraCluster` from
the testing projects, moving them out of the test projects if that is cleaner. Run output goes to `chaos/runs/<run-id>/`
and is ignored by git.

## Event identity

The checker must not depend on store internals. Each event the workload writes carries its own identity in the
payload: run ID, writer ID, operation number, commit ID and index in the batch. An operation is one `AppendAsync` call
and all its retries, so every retry of an operation uses the same commit ID. This also lets the checker detect a
phantom: an event a subscriber received that differs from, or is missing from, the ground truth at that position.

## Writer outcomes

Each attempt ends in one of three outcomes, as in Jepsen's histories:

- **Ok.** The append returned.
- **Failed.** The append definitely did not commit, for example `StreamAppendConflictException`.
- **Indeterminate.** The client cannot know: a timeout, a dropped connection, an unknown commit result.

The writer retries indeterminate attempts with the same commit ID, up to its retry policy. The operation's final
outcome is its last attempt's.

## Invariants

The ground truth G is a full forward `ReadAll` after all faults are healed and the workload has stopped.

| ID  | Invariant                                                                                                  |
|-----|------------------------------------------------------------------------------------------------------------|
| I1  | G's log positions are strictly increasing with no gaps.                                                    |
| I2  | Each stream's positions in G are contiguous and in log order.                                              |
| I3  | No operation's events appear in G more than once.                                                          |
| I4  | Every Ok operation's events appear in G.                                                                   |
| I5  | No Failed operation's events appear in G.                                                                  |
| I6  | Indeterminate operations appear zero or one times. Reported as counts, not a violation.                    |
| I7  | Each subscriber's received events form a contiguous run that matches G position for position (no gaps, no duplicates, no reordering, no phantoms). Across a resubscribe from `After(p)`, the next event is at `p + 1`. |
| I8  | After healing, every running subscriber reaches G's last position within the run's timeout.                |
| I9  | Subscribers expected to give up do so with the expected error within the expected window, and deliver nothing after. Any other give-up is a violation. |

Metrics, not invariants: recovery time from heal to the first Ok append and to each subscriber resuming, append
latency, and subscriber lag. A scenario can set thresholds on these.

I1, I7 and I9 can be checked live as events arrive. I3 to I5 need G, so the live checker shows them as provisional and
the final check is authoritative.

## Scenario files

A topology and a scenario are separate files, so a scenario runs against any topology that supports its faults.

```yaml
scenario: db-outage-single-writer
duration: 3m
workload:
  writers:
    count: 1
    rate: 100/s
    streams: 20
    eventsPerAppend: 1..5
    retry: { attempts: 20, backoff: exponential, max: 5s }
  subscribers:
    count: 1
    origin: start
nemesis:
  - at: 30s
    fault: crash-node
    target: primary
    heal-after: 20s
  - at: 90s
    fault: network
    mode: reset-peer
    heal-after: 10s
expect:
  recovery-within: 60s
  give-ups: none
```

Seeded random faults and interactive faults produce the same run events as scripted ones, so a run's record is the
same whichever way its faults came.

## Scenarios from known failure modes

[Catch-up subscriptions: failure modes](subscription-failure-modes.md) lists failures that were found by reading
source and fixed. Each one should become a scenario, so the fixes stay fixed:

- MongoDB late attacher after a failover: attach a subscriber straight after a step-down (#160)
- PostgreSQL late attacher (#162)
- PostgreSQL phantom and position reuse with standbys (#165, #168)
- PostgreSQL commit visible before the standby has it: cancel a commit waiting on the standby, then fail over (#164)
- Appends taking the session's `synchronous_commit` (#161)
- A feed whose endpoint reaches a standby during failover (#170)
- Transient errors during catch-up, and giving up at `CatchUpMaxRetryAttempts` or `CatchUpRetryTimeout`

Some configurations are expected to fail, such as asynchronous standbys or `w:1`. Those runs are still worth having:
they show a client why the configuration matters.

## Phase 1: one node, end to end, with a UI

Each step is one or more local commits, following the 50/72 rule, small enough to review on its own. Work on a
`feat/chaos` branch from `main`.

1. **Scaffold.** The three projects, solution entries, `chaos/runs/` in `.gitignore`. Verify: the solution builds.
2. **Run events and history.** The run event types (run started and ended, append invoked and completed, event
   received, caught up, fell behind, subscriber faulted, fault injected and healed), each with a monotonic timestamp
   and wall-clock time. A JSONL writer and reader. Verify: round-trip tests.
3. **Final checker.** I1 to I9 over a history and G, with a report naming each violation and its evidence. Verify:
   unit tests with a synthetic history for each violation, plus clean ones. This step matters most, so it gets the
   most tests.
4. **Workload.** A writer as described under writer outcomes, using `ExpectedStreamState` any. A subscriber that
   `SubscribeToAll`s from the start and records what it receives. Verify: a short fault-free run against a MongoDB
   container passes the checker.
5. **Topology and faults.** `ITopology` and `IFault`. First topology: MongoDB, a one-member replica set. Faults:
   crash and restart (`docker kill`), pause and resume, and network faults through a proxy (cut, latency, reset). The
   driver reaches the member through the proxy with `directConnection=true`, otherwise it would discover the member's
   real address and bypass the proxy. Decide here between Toxiproxy and the existing `TcpProxy`. Toxiproxy covers
   more faults, and `TcpProxy` may be extended instead. Verify: each fault by hand, with a test per fault that it
   injects and heals.
6. **Scenario runner.** Loads the YAML, starts the topology, runs the workload and the scripted faults, heals
   everything, waits for quiet, reads G, runs the checker. Verify: the example scenario runs.
7. **CLI.** `dbx-chaos run <scenario> --topology <name> [--seed n] [--out dir]`. Writes `history.jsonl` and
   `report.json`, prints a summary. Exit codes: 0 passed, 1 violations, 2 the run itself failed. Verify: by hand.
8. **Sabotage modes.** A writer that retries with a new commit ID, and a subscriber that skips an event when it
   resumes, chosen by a CLI flag. Verify: both runs fail with the expected violations. Without this, a passing run
   proves nothing.
9. **Live checker.** The live-checkable invariants and provisional counts, run over the run events as they arrive.
   Verify: tests that feed events one at a time.
10. **UI.** `dbx-chaos serve` hosts Blazor Server, with the runner in process. One page: pick a scenario and
    topology, start it, and watch:
    - a position chart: the log's last position, polled by reading backwards from the end, against each subscriber's
      position over time, with fault bands overlaid
    - the invariant panel, live
    - buttons for each fault the topology supports, which inject and heal through the same `IFault` and record the
      same run events

    Plain styling. The design pass comes later. Verify: by eye, during a run with a crash and a network cut.
11. **PostgreSQL, one node.** The same topology and faults for PostgreSQL, and the same scenario run against it.
    Verify: the scenario passes on both stores.
12. **README.** How to run the CLI and the UI, and what each invariant means.

## Later phases

These are sketches. Phase 1 will change them.

**Phase 2: clusters and many clients.**
- MongoDB three-member replica set: step-down, kill the primary, the `failCommand` failpoint (for example a network
  error or unknown commit result on `commitTransaction`), and partitions between members with `docker network
  disconnect` or iptables. Proxying the members needs the replica set configuration to advertise the proxy addresses.
- PostgreSQL with a synchronous standby, building on `ReplicatedPostgresServer`. Then Patroni: a compose setup with
  etcd ×3, PostgreSQL ×3 and HAProxy, with faults via `patronictl` or its REST API, plus killing etcd members.
- Many writers and subscribers. Writers using expected stream state, to cover conflicts.
- Writers and subscribers in separate processes, so client faults are possible: kill a writer during a commit,
  `SIGSTOP` a subscriber, restart a subscriber from its checkpoint.
- The topology view in the UI, with nodes, roles and links that can be clicked to inject faults.

**Phase 3: random faults and give-ups.**
- A seeded random nemesis, with the seed in the report.
- Give-up scenarios with expectations on `CatchUpMaxRetryAttempts` and `CatchUpRetryTimeout`.
- Swimlanes in the UI, with one lane per writer and subscriber and outcomes marked.

**Phase 4: cloud.**
- Aurora: extend `AuroraCluster` with `aurora_inject_crash`, `aurora_inject_replica_failure`,
  `aurora_inject_disk_failure`, `aurora_inject_disk_congestion` and `FailoverDBCluster`, and consider AWS FIS for
  network disruption. A proxy in front of the cluster endpoint can hide its DNS change on failover, so check how it
  resolves the upstream.
- Aurora: test the assumption that logical decoding sends a commit only once storage has it, which AWS doesn't
  document ([PostgreSQL on Aurora](postgresql-on-aurora.md)). Slow storage with `aurora_inject_disk_congestion`, and
  for each event compare when a subscriber receives it with when its append returns. A short lead is a normal race
  between the two connections, but a lead near the injected delay means a subscriber got the event before storage
  had it, so a failover could lose it.
- Atlas: the Admin API's primary failover test and regional outage simulation. Atlas's internal network cannot be
  faulted, so network faults are on the client side only.
- Cloud runs cost time and money, and failovers take tens of seconds to minutes, so scenarios need longer timeouts.

**Phase 5: client-facing.**
- Replay of recorded runs in the UI, so a real Atlas or Aurora failover can be shown without running one live.
- A design pass on the UI, once the views have settled.

## Open decisions

- Toxiproxy or the existing `TcpProxy` (step 5).
- The charting approach in Blazor: plain SVG components, or a JS library such as uPlot through interop (step 10).
- Whether the platform should also run as NUnit tests in CI for the cheap scenarios, or stay CLI only.
