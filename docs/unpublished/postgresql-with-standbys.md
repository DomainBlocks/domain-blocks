# PostgreSQL with standbys

Each standby keeps its own copy of the WAL, so a commit is durable only once the standbys that a failover can promote
have it. The store runs its appends with `synchronous_commit = on`, and a catch-up delivers what it read only once the
synchronous standbys have it. The server has to do the rest:

- Name the standbys that a failover can promote in `synchronous_standby_names`, and use a failover manager that
  promotes only a synchronous standby, such as Patroni in synchronous mode.
- Give each of those standbys a physical replication slot, and list the slots in `synchronized_standby_slots`. The
  primary then holds each commit back from subscriptions until those standbys have it. Without the list,
  subscriptions still work, and the store logs a warning, but a failover can lose an event that a subscription has
  received, and the subscription then skips the event that reuses its position. A listed slot that is missing,
  invalidated or inactive holds subscriptions back without an error: they stop receiving new events, and only a
  warning in the primary's log names the slot.
- Optionally, set `sync_replication_slots = on` on those standbys, with `hot_standby_feedback = on`,
  `wal_level = logical`, and a `dbname` in `primary_conninfo`. They then keep a copy of the store's slot, and after a
  failover subscriptions resume from it instead of catching up.
- Keep the lists in step, on whichever server is the primary. `synchronous_standby_names` names standbys by
  application name and `synchronized_standby_slots` by slot name, and PostgreSQL doesn't check that they agree. The
  slot list also waits for every standby it names, while synchronous replication needs only as many as
  `synchronous_standby_names` asks for. So a listed standby that goes offline holds subscriptions back, even while
  appends carry on through the others. List only the synchronous standbys that are connected, and update the list
  whenever that set changes: after a failover, and when a standby goes offline or comes back. Patroni does this when
  `manage_synchronized_standby_slots` is on, with `synchronous_mode`. The option was added after Patroni 4.1.5, in
  [patroni/patroni#3527](https://github.com/patroni/patroni/pull/3527). In quorum mode it lists every synchronous
  standby, so one that falls behind still holds subscriptions back. Without such a manager, the list is yours to
  maintain, and while it's empty, a failover can lose an event that a subscription has received.
