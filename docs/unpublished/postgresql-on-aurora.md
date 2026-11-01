# PostgreSQL on Amazon Aurora

Aurora keeps its data in a cluster volume that every instance shares, and a commit is durable once that volume has
it. The writer also sends its log to the readers, but only to keep their page caches current, so there are no
standbys to set up. A commit is durable before any query can see it, and the cluster endpoint follows the writer.

## Setup

- Use Aurora PostgreSQL 17 or later.
- Set `rds.logical_replication = 1` in the cluster parameter group, which is how Aurora sets `wal_level = logical`. It
  takes effect after a reboot.
- Grant the store's role `rds_replication`, which stands in for the `REPLICATION` attribute.
- Connect to the cluster endpoint. During a failover it can reach the old writer for a while, now a reader, and
  subscriptions keep retrying until it reaches the new writer.

## Failover

The store creates its replication slot with PostgreSQL's failover option, which has no effect on Aurora. Aurora keeps
slots in the cluster volume instead, so after a writer failover subscriptions resume on the new writer without
catching up. AWS doesn't document this, but the [Aurora tests][tests] check it. If a failover did lose the
slot, subscriptions would catch up from where they were and miss nothing.

## An assumption still to validate

The store also relies on something that hasn't been validated: that Aurora's logical replication sends a commit only
once its storage has it, as Aurora's queries see a commit only then. If it sent one earlier, a failover could lose an
event that a subscription had already received. AWS doesn't document this, and the [Aurora tests][tests] can't produce
the opposite, so treat it as unproven until both of these checks are done:

- Ask AWS.
- Slow Aurora's storage with its fault injection functions, and compare when a subscriber receives each event with
  when its append returns. A short lead is a normal race between the two connections, but a lead near the injected
  delay would mean that the assumption is wrong. The [chaos platform plan](chaos-platform-plan.md) includes this test.

[tests]: https://github.com/DomainBlocks/domain-blocks/tree/main/tests/DomainBlocks.EventStore.PostgreSQL.Tests.Aurora
