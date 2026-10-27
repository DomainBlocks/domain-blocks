# Live subscriptions: push vs pull

> **Generated note.** Written by Claude Code (Claude Opus 5.5) on 24 October 2026 at the request of the author. It
> compares push delivery as of #171 with pull delivery as of #149, on PostgreSQL only.

## What was compared

- **Push** (as of #171). The server pushes each new event to every store instance. Each instance reads logical
  replication through its own failover slot, so each has a walsender that decodes all of the database's WAL. With
  synchronous standbys whose slots are listed in `synchronized_standby_slots`, the server holds each commit back from
  the slot until the standbys have it.
- **Pull** (as of #149). Each store instance pulls new events from the log. Each append notifies a channel, and each
  instance's tail reads the log when it hears one, or every second. With synchronous standbys, a tail read that finds
  rows commits a barrier, a small transaction that writes WAL and waits for the standbys, before it delivers them.

## Findings

- **Both are correct under load.** Every subscriber in every run received every event once, at increasing log
  positions.
- **Push delivers sooner.** Its p50 and p99 are lower in every configuration. With a synchronous standby, push waits
  for the standby once per event, and pull waits twice: for the append's commit, then for its barrier.
- **Push's slowest deliveries are sometimes slower.** Its max, the single slowest delivery in a run, is higher in 9 of
  27 configurations, 7 of them with 16 instances, and by up to 16 ms (32 ms against 16 ms).
- **Pull uses more server CPU, and the gap grows with instances.** Pull uses 1.3 times push's CPU with one instance at
  100 events per second, and 2.4 times with 16. A synchronous standby barely changes the ratios. Pull's tails run
  about two queries per event per instance, while push's walsenders send the rows.
- **Unrelated writes narrow the gap.** Every walsender decodes all of the database's WAL, not just the event log's, so
  with 2000 unrelated inserts per second, pull uses only 1.0 to 1.4 times push's CPU. With heavier unrelated traffic
  and more instances, push could use more; that wasn't measured.

Unloaded latency in ms, from the start of an append to its delivery, one append at a time:

| Setup                      | Push p50 | Pull p50 | Push p99 | Pull p99 |
|----------------------------|----------|----------|----------|----------|
| Single server              | 1.88     | 2.55     | 2.61     | 3.45     |
| Synchronous standby        | 2.61     | 4.54     | 3.43     | 6.06     |
| Synchronous standby, +1 ms | 3.59     | 6.54     | 4.49     | 7.82     |

Adding 1 ms to each round trip to the standby costs push 0.98 ms and pull 2.00 ms.

Server CPU, in cores, added by each store instance beyond the first (from 1 to 16 instances; with a standby, primary
plus standby):

| Load                       | Push, single | Pull, single | Push, standby | Pull, standby |
|----------------------------|--------------|--------------|---------------|---------------|
| 100 events/s               | 0.012        | 0.036        | 0.016         | 0.052         |
| 1000 events/s              | 0.025        | 0.073        | 0.034         | 0.092         |
| 100 events/s, other writes | 0.020        | 0.040        | 0.032         | 0.049         |

## Method

- **Harness.** One console program, compiled against each branch, using only the public API that both share. Each
  store instance has one `SubscribeToAll` from the end of the log. A separate store appends one event per append, each
  to its own stream, at a paced rate.
- **Configurations.** 1, 4 or 16 instances; idle, 100 or 1000 events per second; and 100 events per second with 2000
  unrelated inserts per second into a table outside the store's schema ("other writes"). Unloaded latency uses one
  instance, one append at a time, and 2000 samples.
- **Runs.** 5 s of warm-up, then 20 s measured. Two runs per variant per configuration, in ABBA order, alternating
  which variant starts.
- **Measurements.** Latency runs from the start of an append to the subscriber receiving the event. Server CPU comes
  from each container's cgroup, split by process type from `/proc`.
- **Data.** Each variant had its own schema, prefilled with 1M events and analyzed. On a small, new log, pull's tail
  read ran as sequential scans, which a real log wouldn't.
- **Servers.** PostgreSQL 17.11 in Docker Desktop on Windows, on a 32-core machine that also ran the client. The
  replicated setup is a primary and one synchronous standby, set up as
  [PostgreSQL with standbys](postgresql-with-standbys.md) describes: a physical slot, `synchronous_standby_names`,
  `synchronized_standby_slots`, and `sync_replication_slots` on the standby. The extra 1 ms was added with
  `tc netem` on the standby's outgoing traffic, and took a synchronous commit from 1.34 to 2.37 ms.

Not covered:

- **The network between client and server.** Everything ran on one machine. Pull adds a round trip per wake-up, the
  notification and then the query, where push's walsender sends the rows.
- **Other setups.** More than 16 instances, more than one standby, catch-up reads, and MongoDB.
- **Client CPU.** It varied too much between runs to compare, except with 16 instances, where push's was lower.

## Single server

Server CPU, in cores. The ratio is pull's CPU over push's.

| Load                       | Instances | Push | Pull | Ratio |
|----------------------------|-----------|------|------|-------|
| 100 events/s               | 1         | 0.10 | 0.13 | 1.3   |
|                            | 4         | 0.14 | 0.25 | 1.8   |
|                            | 16        | 0.28 | 0.67 | 2.4   |
| 1000 events/s              | 1         | 0.16 | 0.26 | 1.7   |
|                            | 4         | 0.22 | 0.52 | 2.4   |
|                            | 16        | 0.53 | 1.35 | 2.5   |
| 100 events/s, other writes | 1         | 0.43 | 0.44 | 1.0   |
|                            | 4         | 0.47 | 0.56 | 1.2   |
|                            | 16        | 0.72 | 1.04 | 1.4   |

Idle, both stay under 0.02 cores.

Where the CPU goes, with 16 instances at 100 events/s:

|                                | Push | Pull  |
|--------------------------------|------|-------|
| Backends: appends and queries  | 0.12 | 0.65  |
| Walsenders                     | 0.16 | none  |
| Total, with background workers | 0.28 | 0.67  |
| Transactions per second        | 160  | 3,279 |

Delivery latency, in ms:

| Load                       | Instances | Push p50 | Pull p50 | Push p99 | Pull p99 | Push max | Pull max |
|----------------------------|-----------|----------|----------|----------|----------|----------|----------|
| 100 events/s               | 1         | 2.35     | 2.99     | 4.86     | 5.80     | 6.8      | 8.0      |
|                            | 4         | 2.59     | 3.44     | 5.41     | 6.74     | 7.1      | 8.2      |
|                            | 16        | 2.94     | 4.09     | 6.29     | 7.78     | **13.7** | 9.5      |
| 1000 events/s              | 1         | 3.42     | 3.73     | 5.46     | 6.27     | 7.1      | 7.5      |
|                            | 4         | 4.06     | 4.25     | 5.99     | 7.39     | 8.9      | 9.7      |
|                            | 16        | 4.71     | 5.65     | 7.07     | 8.86     | **13.1** | 11.6     |
| 100 events/s, other writes | 1         | 2.99     | 3.63     | 5.38     | 6.00     | 6.6      | 7.3      |
|                            | 4         | 3.13     | 3.91     | 5.69     | 6.41     | **15.1** | 8.0      |
|                            | 16        | 3.59     | 4.66     | 6.44     | 7.92     | **14.9** | 10.4     |

Bold marks a max where push's is higher.

## Synchronous standby

Server CPU, in cores, primary plus standby, with no added delay. The ratio is pull's CPU over push's, also with 1 ms
added to each round trip to the standby.

| Load                       | Instances | Push | Pull | Ratio | Ratio, +1 ms |
|----------------------------|-----------|------|------|-------|--------------|
| 100 events/s               | 1         | 0.19 | 0.25 | 1.3   | 1.4          |
|                            | 4         | 0.21 | 0.37 | 1.8   | 2.0          |
|                            | 16        | 0.42 | 1.02 | 2.4   | 2.5          |
| 1000 events/s              | 1         | 0.25 | 0.39 | 1.5   | 1.6          |
|                            | 4         | 0.33 | 0.72 | 2.2   | 2.0          |
|                            | 16        | 0.76 | 1.77 | 2.3   | 2.1          |
| 100 events/s, other writes | 1         | 0.56 | 0.61 | 1.1   | 1.1          |
|                            | 4         | 0.65 | 0.77 | 1.2   | 1.2          |
|                            | 16        | 1.04 | 1.34 | 1.3   | 1.4          |

Where the CPU goes, with 16 instances at 100 events/s. Pull's walsender serves the standby. Push's walsenders cost more
than on the single server, and pull's barriers write more WAL, which the standby has to receive and flush.

|                         | Push | Pull  |
|-------------------------|------|-------|
| Primary backends        | 0.12 | 0.87  |
| Primary walsenders      | 0.26 | 0.06  |
| Standby                 | 0.03 | 0.07  |
| Transactions per second | 154  | 3,243 |
| WAL, kB/s               | 55   | 198   |

Delivery latency, in ms, with no added delay:

| Load                       | Instances | Push p50 | Pull p50 | Push p99 | Pull p99 | Push max | Pull max |
|----------------------------|-----------|----------|----------|----------|----------|----------|----------|
| 100 events/s               | 1         | 4.12     | 5.89     | 8.34     | 9.95     | 10.6     | 22.9     |
|                            | 4         | 3.49     | 5.80     | 7.45     | 10.05    | 10.0     | 18.4     |
|                            | 16        | 3.98     | 7.04     | 8.48     | 12.28    | 17.6     | 24.9     |
| 1000 events/s              | 1         | 5.40     | 5.89     | 8.70     | 9.87     | 20.5     | 20.7     |
|                            | 4         | 4.70     | 7.10     | 8.55     | 11.06    | **31.7** | 16.1     |
|                            | 16        | 5.90     | 8.63     | 9.43     | 12.97    | **17.9** | 16.3     |
| 100 events/s, other writes | 1         | 4.18     | 6.14     | 7.08     | 9.63     | 9.1      | 17.9     |
|                            | 4         | 4.40     | 6.85     | 7.54     | 10.84    | 10.6     | 13.9     |
|                            | 16        | 4.78     | 7.48     | 8.31     | 12.49    | **17.7** | 15.8     |

Delivery latency, in ms, with 1 ms added to each round trip to the standby:

| Load                       | Instances | Push p50 | Pull p50 | Push p99 | Pull p99 | Push max | Pull max |
|----------------------------|-----------|----------|----------|----------|----------|----------|----------|
| 100 events/s               | 1         | 4.04     | 6.94     | 8.54     | 11.56    | 10.2     | 12.8     |
|                            | 4         | 4.39     | 8.06     | 9.07     | 13.48    | 12.6     | 15.5     |
|                            | 16        | 4.80     | 8.90     | 9.89     | 14.66    | 13.1     | 16.2     |
| 1000 events/s              | 1         | 5.39     | 7.95     | 9.52     | 12.58    | 11.7     | 15.3     |
|                            | 4         | 5.79     | 9.20     | 9.92     | 13.77    | 12.8     | 17.1     |
|                            | 16        | 8.35     | 10.70    | 11.16    | 15.62    | **20.1** | 19.4     |
| 100 events/s, other writes | 1         | 5.01     | 7.78     | 8.91     | 12.00    | 10.8     | 13.4     |
|                            | 4         | 5.03     | 8.55     | 9.15     | 13.59    | 12.5     | 17.0     |
|                            | 16        | 5.54     | 9.28     | 10.05    | 14.99    | **19.3** | 17.1     |

## Discarded runs

- **The first runs with other writes.** The generator fell short of its rate and gave the two variants different
  loads. It was rewritten and the runs repeated.
- **Two pull runs that stalled appends.** For about 0.75 s on the single server (4 instances, other writes), and about
  4.5 s with the standby (+1 ms, 1 instance, 1000 events per second). That is 2 of 84 pull runs, and none of 84 push
  runs. Reruns of both configurations were clean and replaced them. The cause wasn't investigated.
- **Comparisons between the standby's two delays.** CPU and WAL moved between the runs with no added delay and those
  with 1 ms added, for both variants alike, so the tables compare the variants only within each delay.
