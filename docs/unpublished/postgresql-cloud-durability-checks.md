# Cloud durability checks for PostgreSQL

Written by Claude (Opus 5.5) in a Claude Code session on 18 October 2026, at the request of the repository author.
Unpublished; it reflects the tests as they were run on that date.

The PostgreSQL store relies on two things that managed services could change:

1. **A commit becomes visible only once it is durable.** In vanilla PostgreSQL the commit is flushed, then waits for
   synchronous standbys, and only then leaves the procarray and becomes visible (`xact.c`). The replication barrier
   runs only when `synchronous_standby_names` is non-empty, because with no standbys every visible append is already
   flushed. On Aurora the setting is empty and the durable step is the storage quorum, so the barrier's skip is only
   safe if Aurora also makes a commit visible after its quorum acknowledges it. AWS documents the acknowledgement to
   the client, not visibility to other sessions.
2. **An ordinary role can pin durability per transaction.** `append_events` runs
   `set_config('synchronous_commit', 'on', true)`, and the barrier calls `pg_logical_emit_message`. If a provider
   rejected either, appends or reads would fail there.

See [Catch-up subscriptions: failure modes](subscription-failure-modes.md) for why these matter.

## Findings

| | Aurora PostgreSQL 17.11, tested | Azure Flexible Server, researched | Cloud SQL, researched |
|---|---|---|---|
| Durable step | Storage quorum; **invisible until done** | Synchronous standby | Regional disk |
| `synchronous_standby_names` | **Empty**: barrier skipped | Non-empty with HA: barrier runs | Empty: barrier skipped |
| Session `set_config` | **Allowed**: `on`, `local`, `off` | Documented as allowed | No restriction documented |
| `pg_logical_emit_message` | **Allowed** for an ordinary role | No restriction documented | No restriction documented |

- **Aurora makes a commit visible only after its storage quorum has acknowledged it,** as vanilla PostgreSQL does after
  its flush or standby wait. The design holds on Aurora as built, and no "always on" barrier is needed.
- **Azure and Cloud SQL were researched, not tested,** for lack of accounts. Running the [checks](#the-checks) as the
  application's role settles them.
- **Aurora refuses `pg_current_wal_lsn()`** unless `wal_level` is `logical` ("WAL control functions cannot be executed
  when wal_level < logical"). The store doesn't call it.

## Aurora

### Method

Writers commit single-row inserts as fast as they can. On the same server, a PL/pgSQL loop repeatedly takes three
steps, microseconds apart:

1. `s0`, a snapshot (`pg_current_snapshot()`);
2. the transaction IDs of the active client backends whose wait event is `XactSync`, read after
   `pg_stat_clear_snapshot()` so that `pg_stat_activity` isn't served from the transaction's cache;
3. `s1`, a second snapshot.

A VOLATILE function under READ COMMITTED takes a new snapshot for every statement, so `s0` and `s1` are distinct.
Each waiting backend falls into one of four cases:

| Case | Meaning |
|---|---|
| `invisible_after` | Its xid is still in progress in `s1`, taken after it was seen waiting: invisible while waiting |
| `visible_before` | Its xid is already visible in `s0`, taken before it was seen waiting: visible too early |
| `inconclusive` | Visible in `s1` but not `s0`: it finished between the steps |
| `null_xid` | No xid while waiting |

If Aurora made a commit visible before its quorum wait, no waiting backend could be `invisible_after`: its xid would
be visible already, or cleared from the procarray. With vanilla ordering, nearly every sample is `invisible_after`
and none is `visible_before`.

`IO:XactSync` is the wait "for the Aurora storage subsystem to acknowledge the commit of a regular transaction"
([AWS](https://docs.aws.amazon.com/AmazonRDS/latest/AuroraUserGuide/apg-waits.xactsync.html)). The same page says it
can also appear while "a query is waiting for another transaction to commit". That doesn't arise here, because the
writers insert distinct `bigserial` keys and never wait on each other.

`pg_stat_activity` reads `backend_xid` when it copies the backend status table, and `wait_event` later, live, from
each backend. That is the likely explanation for the few `null_xid` samples: a backend seen before it was assigned an
xid, and seen waiting a moment later. Vanilla PostgreSQL shows them too.

### Results

16 writers, 60 seconds per run. The client saw a median commit latency of about 58 ms, most of it network:

| Run | Commits | Samples | Waiting | `invisible_after` | `inconclusive` | `visible_before` | `null_xid` |
|---|---|---|---|---|---|---|---|
| Aurora | 16,995 | 700,615 | 1,061,534 | 1,056,377 | 4,969 | **0** | 188 |
| Aurora, storage congestion | 17,005 | 703,548 | 1,062,918 | 1,057,794 | 4,949 | **0** | 175 |
| Vanilla 17, local flush (`WalSync`), 8 writers, 5 s | 37,209 | 391,135 | 351,001 | 350,947 | 46 | **0** | 8 |
| Vanilla 17, missing sync standby (`SyncRep`), 8 writers, 5 s | 0 | 362,951 | 2,903,608 | 2,903,608 | 0 | **0** | 0 |

- On average 1.5 of the 16 writers were in `XactSync` at any moment, so each commit spent about 5 ms waiting for the
  quorum, out of the 58 ms the client saw.
- **Storage congestion made no measurable difference.** `aurora_inject_disk_congestion(100, i, true, 75, 50, 100)` on
  each of the volume's 96 protection groups left the commit count and the client latency unchanged (59.8 ms against
  58.4 ms median). A commit needs only four of its six copies, so delaying some copies doesn't hold it.
- **A first attempt sampled from the client** with three round trips per sample. Of 708 waits, 705 were inconclusive,
  because each commit finished long before the second snapshot arrived. The other three were idle backends whose
  `wait_event` was read after they had moved on. That is why the sampler runs on the server.

This is sampling evidence. It shows the order for ordinary commits on Aurora PostgreSQL 17.11, which is what appends
and barriers are. Rerun it after major version upgrades.

### Setup

The scripts ran from Git Bash on Windows, with the AWS CLI v2 and Python 3.14 through `uv` with psycopg 3. The copies
here drop the scratch paths and progress messages of the ones run. `create.sh` creates:

- a security group in the default VPC that admits port 5432 from this machine's public IP only;
- the cluster `dbx-aurora-test`: Aurora PostgreSQL 17.11, I/O-Optimized storage (no per-I/O charges under the commit
  load), Serverless v2 at 0 to 2 capacity units, pausing after 10 idle minutes, without deletion protection;
- one publicly accessible `db.serverless` instance, `dbx-aurora-test-1`.

At eu-west-3 prices in October 2026, I/O-Optimized Serverless v2 costs $0.19 per capacity unit-hour, so the cluster
costs at most $0.38 an hour at 2 units, and the whole test, about 40 minutes, cost cents.

`create.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
WORK="${WORK:-$PWD}"
export AWS_REGION=eu-west-3
VPC=$(aws ec2 describe-vpcs --filters Name=isDefault,Values=true --query "Vpcs[0].VpcId" --output text)
IP=$(curl -s https://checkip.amazonaws.com | tr -d '[:space:]')

SG=$(aws ec2 create-security-group --group-name dbx-aurora-test --description "Temporary Aurora visibility test" \
  --vpc-id "$VPC" --query GroupId --output text)
aws ec2 authorize-security-group-ingress --group-id "$SG" --protocol tcp --port 5432 --cidr "$IP/32" >/dev/null
printf '%s' "$SG" > "$WORK/sg.txt"

PW=$(python -c "import secrets, string; a = string.ascii_letters + string.digits; \
print(''.join(secrets.choice(a) for _ in range(28)))")
printf '%s' "$PW" > "$WORK/pw.txt"

aws rds create-db-cluster --db-cluster-identifier dbx-aurora-test --engine aurora-postgresql --engine-version 17.11 \
  --master-username dbxadmin --master-user-password "$PW" --vpc-security-group-ids "$SG" \
  --storage-type aurora-iopt1 \
  --serverless-v2-scaling-configuration MinCapacity=0,MaxCapacity=2,SecondsUntilAutoPause=600 \
  --backup-retention-period 1 --no-deletion-protection --tags Key=purpose,Value=dbx-visibility-test \
  --query "DBCluster.[DBClusterIdentifier,Status,Endpoint]" --output text

aws rds create-db-instance --db-instance-identifier dbx-aurora-test-1 --db-cluster-identifier dbx-aurora-test \
  --engine aurora-postgresql --db-instance-class db.serverless --publicly-accessible \
  --query "DBInstance.[DBInstanceIdentifier,DBInstanceStatus]" --output text
```

The instance took about 10 minutes to become available:

```bash
aws rds wait db-instance-available --db-instance-identifier dbx-aurora-test-1 --region eu-west-3
```

Every script below takes a libpq connection string. The cluster endpoint is printed by `create.sh`:

```bash
H=dbx-aurora-test.cluster-<id>.eu-west-3.rds.amazonaws.com
ADMIN="host=$H user=dbxadmin password=$(cat pw.txt) dbname=postgres sslmode=require"
APP="host=$H user=dbx_app password=dbx_app_pw_1 dbname=postgres sslmode=require"
uv run -q --no-project --with "psycopg[binary]" python -I checks.py --admin-dsn "$ADMIN" --app-dsn "$APP"
uv run -q --no-project --with "psycopg[binary]" python -I visibility.py --dsn "$ADMIN" --writers 16 --seconds 60
uv run -q --no-project --with "psycopg[binary]" python -I visibility.py --dsn "$ADMIN" --writers 16 --seconds 60 \
  --inject-congestion
```

### Teardown

`teardown.sh` deletes the instance, then the cluster without a final snapshot, then the security group. Afterwards no
clusters, instances, snapshots or retained automated backups were left.

```bash
#!/usr/bin/env bash
set -uo pipefail
WORK="${WORK:-$PWD}"
export AWS_REGION=eu-west-3

aws rds delete-db-instance --db-instance-identifier dbx-aurora-test-1 --skip-final-snapshot \
  --query "DBInstance.DBInstanceStatus" --output text
aws rds wait db-instance-deleted --db-instance-identifier dbx-aurora-test-1

aws rds delete-db-cluster --db-cluster-identifier dbx-aurora-test --skip-final-snapshot \
  --query "DBCluster.Status" --output text
until ! aws rds describe-db-clusters --db-cluster-identifier dbx-aurora-test >/dev/null 2>&1; do sleep 15; done

aws ec2 delete-security-group --group-id "$(cat "$WORK/sg.txt")"
aws rds describe-db-clusters --query "DBClusters[].DBClusterIdentifier" --output text
aws rds describe-db-cluster-automated-backups --query "DBClusterAutomatedBackups[].DBClusterIdentifier" --output text
```

### Gotchas

- **The master user isn't a superuser.** `CREATE SCHEMA ... AUTHORIZATION dbx_app` fails with "must be able to SET ROLE
  "dbx_app"" until the master user is granted the role, which `checks.py` does.
- **The fault injection functions are hidden.** They don't appear in `pg_proc`, but calling them works.
  `aurora_show_volume_status()` returned 96 protection groups (`disks`) and 47 storage nodes (`nodes`) for a nearly
  empty volume.
- **Validate the sampler against vanilla PostgreSQL first.** To hold every commit in `SyncRep`, start `postgres:17`,
  create the table and function, then `ALTER SYSTEM SET synchronous_standby_names = 'ghost'` and reload. Don't pass
  the setting with `-c` at startup: command-line settings override `ALTER SYSTEM`, so the setup's own commits hang.

## The checks

`checks.py` creates an ordinary role, `dbx_app`, and runs as it every call the store relies on, each in its own
transaction. The same script settles Azure and Cloud SQL.

```python
"""What an ordinary application role can do that the event store relies on."""
import argparse

import psycopg

p = argparse.ArgumentParser()
p.add_argument("--admin-dsn", required=True)
p.add_argument("--app-dsn", required=True, help="DSN for role dbx_app with password 'dbx_app_pw_1'")
args = p.parse_args()

with psycopg.connect(args.admin_dsn, autocommit=True) as c:
    if not c.execute("SELECT 1 FROM pg_roles WHERE rolname = 'dbx_app'").fetchone():
        c.execute("CREATE ROLE dbx_app LOGIN PASSWORD 'dbx_app_pw_1'")
    c.execute("GRANT dbx_app TO CURRENT_USER")
    c.execute("CREATE SCHEMA IF NOT EXISTS dbx AUTHORIZATION dbx_app")
    print("admin: version =", c.execute("SELECT version()").fetchone()[0])
    for name in ("synchronous_commit", "synchronous_standby_names", "wal_level", "fsync"):
        print(f"admin: {name} =", c.execute("SELECT current_setting(%s, true)", (name,)).fetchone()[0])

checks = [
    ("synchronous_commit", "SELECT current_setting('synchronous_commit')"),
    ("synchronous_standby_names", "SELECT current_setting('synchronous_standby_names')"),
    ("set_config on, local", "SELECT set_config('synchronous_commit', 'on', true)"),
    ("set_config local, local", "SELECT set_config('synchronous_commit', 'local', true)"),
    ("set_config off, local", "SELECT set_config('synchronous_commit', 'off', true)"),
    ("execute pg_logical_emit_message",
     "SELECT p.oid::regprocedure::text, has_function_privilege(p.oid, 'EXECUTE') FROM pg_proc p "
     "WHERE p.proname = 'pg_logical_emit_message'"),
    ("pg_logical_emit_message(true, ...)", "SELECT pg_logical_emit_message(true, 'dbx_barrier', '')"),
    ("the barrier, as EventLogSql runs it",
     "SELECT set_config('synchronous_commit', 'on', true), pg_logical_emit_message(true, 'dbx_barrier', '') "
     "WHERE current_setting('synchronous_standby_names') <> '' AND EXISTS (SELECT 1)"),
    ("pg_current_wal_lsn", "SELECT pg_current_wal_lsn()"),
]

with psycopg.connect(args.app_dsn) as c:
    for name, sql in checks:
        try:
            with c.transaction():
                rows = c.execute(sql).fetchall()
                after = c.execute("SELECT current_setting('synchronous_commit')").fetchone()[0]
            print(f"app: {name}: OK {rows} (synchronous_commit in the transaction: {after})")
        except Exception as e:  # noqa: BLE001
            print(f"app: {name}: FAILED {type(e).__name__}: {e}".strip())
    c.execute("""
        CREATE OR REPLACE FUNCTION dbx.pinned_append() RETURNS text LANGUAGE plpgsql AS $$
        BEGIN
            PERFORM set_config('synchronous_commit', 'on', true);
            RETURN current_setting('synchronous_commit');
        END $$""")
    c.commit()
    c.execute("SET synchronous_commit = off")
    print("app: function pinning on, session off ->", c.execute("SELECT dbx.pinned_append()").fetchone()[0])
    c.commit()
```

On Aurora:

```text
admin: version = PostgreSQL 17.11 on aarch64-linux, compiled by gcc-10.5.0, 64-bit
admin: synchronous_commit = on
admin: synchronous_standby_names =
admin: wal_level = replica
admin: fsync = on
app: synchronous_commit: OK [('on',)] (synchronous_commit in the transaction: on)
app: synchronous_standby_names: OK [('',)] (synchronous_commit in the transaction: on)
app: set_config on, local: OK [('on',)] (synchronous_commit in the transaction: on)
app: set_config local, local: OK [('local',)] (synchronous_commit in the transaction: local)
app: set_config off, local: OK [('off',)] (synchronous_commit in the transaction: off)
app: execute pg_logical_emit_message: OK [('pg_logical_emit_message(boolean,text,bytea,boolean)', True),
     ('pg_logical_emit_message(boolean,text,text,boolean)', True)] (synchronous_commit in the transaction: on)
app: pg_logical_emit_message(true, ...): OK [('0/565F08E',)] (synchronous_commit in the transaction: on)
app: the barrier, as EventLogSql runs it: OK [] (synchronous_commit in the transaction: on)
app: pg_current_wal_lsn: FAILED ObjectNotInPrerequisiteState: wal_level must be set to 'logical'
HINT:  WAL control functions cannot be executed when wal_level < logical.
app: function pinning on, session off -> on
```

The barrier returned no row because `synchronous_standby_names` is empty, so it wrote nothing.

## The visibility sampler

`visibility.py` starts the writers, waits two seconds, then runs the sampler for the given time. It is the script
behind the results above.

```python
import argparse
import threading
import time
from collections import Counter

import psycopg

p = argparse.ArgumentParser()
p.add_argument("--dsn", required=True)
p.add_argument("--wait-event", default="XactSync")
p.add_argument("--writers", type=int, default=16)
p.add_argument("--seconds", type=int, default=60)
p.add_argument("--inject-congestion", action="store_true",
               help="Aurora only: delay every protection group's I/O by 50-100 ms for the run")
args = p.parse_args()

SAMPLER = """
CREATE OR REPLACE FUNCTION vis_sample(seconds float8, wait text)
RETURNS TABLE (kind text, n bigint) LANGUAGE plpgsql VOLATILE AS $$
DECLARE
    deadline timestamptz := clock_timestamp() + make_interval(secs => seconds);
    s0 pg_snapshot;
    s1 pg_snapshot;
    xids text[];
    polls bigint := 0;
    waiting bigint := 0;
    null_xid bigint := 0;
    visible_before bigint := 0;
    invisible_after bigint := 0;
    inconclusive bigint := 0;
    x text;
BEGIN
    WHILE clock_timestamp() < deadline LOOP
        s0 := pg_current_snapshot();
        PERFORM pg_stat_clear_snapshot();
        SELECT array_agg(a.backend_xid::text) INTO xids FROM pg_stat_activity a
        WHERE a.wait_event = wait AND a.state = 'active' AND a.backend_type = 'client backend';
        s1 := pg_current_snapshot();
        polls := polls + 1;
        IF xids IS NOT NULL THEN
            FOREACH x IN ARRAY xids LOOP
                waiting := waiting + 1;
                IF x IS NULL THEN
                    null_xid := null_xid + 1;
                ELSIF pg_visible_in_snapshot(x::xid8, s0) THEN
                    visible_before := visible_before + 1;
                ELSIF NOT pg_visible_in_snapshot(x::xid8, s1) THEN
                    invisible_after := invisible_after + 1;
                ELSE
                    inconclusive := inconclusive + 1;
                END IF;
            END LOOP;
        END IF;
    END LOOP;
    RETURN QUERY VALUES ('polls', polls), ('waiting', waiting), ('invisible_after', invisible_after),
        ('inconclusive', inconclusive), ('visible_before', visible_before), ('null_xid', null_xid);
END $$
"""

with psycopg.connect(args.dsn, autocommit=True) as c:
    if not c.execute("SELECT 1 FROM pg_proc WHERE proname = 'vis_sample'").fetchone():
        c.execute("CREATE TABLE IF NOT EXISTS vis_t (id bigserial PRIMARY KEY, v int NOT NULL)")
        c.execute(SAMPLER)
    if args.inject_congestion:
        disks = c.execute("SELECT disks FROM aurora_show_volume_status()").fetchone()[0]
        for i in range(disks):
            c.execute("SELECT aurora_inject_disk_congestion(100, %s, true, %s, 50, 100)", (i, args.seconds + 15))
        print(f"injected congestion on {disks} protection group(s)")

stop = threading.Event()
commits = Counter()
latency = []
errors = []


def writer(n):
    try:
        with psycopg.connect(args.dsn, autocommit=True) as c:
            c.execute("SET synchronous_commit = on")
            while not stop.is_set():
                started = time.perf_counter()
                c.execute("INSERT INTO vis_t (v) VALUES (%s)", (n,))
                latency.append(time.perf_counter() - started)
                commits[n] += 1
    except Exception as e:  # noqa: BLE001
        errors.append(repr(e))


threads = [threading.Thread(target=writer, args=(n,), daemon=True) for n in range(args.writers)]
for t in threads:
    t.start()
time.sleep(2)

with psycopg.connect(args.dsn, autocommit=True) as c:
    result = dict(c.execute("SELECT * FROM vis_sample(%s, %s)", (args.seconds, args.wait_event)).fetchall())
    waits = c.execute(
        "SELECT wait_event_type || ':' || wait_event, count(*) FROM pg_stat_activity "
        "WHERE backend_type = 'client backend' AND wait_event IS NOT NULL GROUP BY 1").fetchall()

stop.set()
join_deadline = time.monotonic() + 10
for t in threads:
    t.join(timeout=max(0.0, join_deadline - time.monotonic()))

latency.sort()
median = latency[len(latency) // 2] * 1000 if latency else float("nan")
print(f"wait event {args.wait_event!r}, {args.writers} writers, {args.seconds} s, congestion={args.inject_congestion}")
print(f"commits: {sum(commits.values())}, median client latency {median:.1f} ms, writer errors: {errors[:3]}")
print(f"waits at the end: {waits}")
for k in ("polls", "waiting", "invisible_after", "inconclusive", "visible_before", "null_xid"):
    print(f"  {k:16} {result[k]}")
```

The vanilla runs used `--wait-event WalSync` and `--wait-event SyncRep`. The `xid8` cast assumes the xid epoch is
still 0, which holds on a new cluster.

## Azure Flexible Server and Cloud SQL

These come from the providers' documentation, read on 18 October 2026, and are untested.

**Azure Database for PostgreSQL Flexible Server**

- **`synchronous_commit` is a read-only server parameter, `on`,** for every PostgreSQL version: "Allowed values `on`
  | Parameter type read-only"
  ([parameters](https://learn.microsoft.com/en-us/azure/postgresql/parameters/parameters-write-ahead-log-settings)).
- **A session `SET` or `set_config` is exempt:** "Use the `SET` command to make session-specific adjustments. ... the
  limitations for modifiable and read-only parameters described earlier don't apply. The corresponding SQL function
  is `set_config(setting_name, new_value, is_local)`"
  ([server parameters](https://learn.microsoft.com/en-us/azure/postgresql/parameters/concepts-parameters)). The
  pinned append should therefore work, and change nothing.
- **High availability uses PostgreSQL's synchronous replication:** "The primary server streams these logs to the
  standby server by using the Postgres streaming protocol. When the standby server storage persists the logs, the
  primary server acknowledges write completion." A WAL replica in another zone keeps a quorum: "In scenarios where
  the standby server is temporarily unavailable, transactions are committed on the primary server and the WAL replica
  server to ensure durability"
  ([high availability][azure-ha]).
  So `synchronous_standby_names` should be non-empty with HA, and the barrier runs. Its value isn't documented, nor
  whether Azure ever empties it, for example while it builds a new standby after a failover.

**Google Cloud SQL for PostgreSQL**

- **`synchronous_commit` isn't a supported flag** ([flags](https://docs.cloud.google.com/sql/docs/postgres/flags)),
  and no restriction on a session `SET` is documented. The parameter is settable by any role in PostgreSQL itself.
- **High availability replicates at the disk:** "Through synchronous replication to each zone's persistent disk, all
  writes made to the primary instance are replicated to disks in both zones before a transaction is reported as
  committed" ([high availability](https://docs.cloud.google.com/sql/docs/postgres/high-availability)). So
  `synchronous_standby_names` should be empty, and the barrier is skipped, which is correct because the local flush
  is the cross-zone write.
- **A gap the store can't see:** when one zone fails, a regional disk acknowledges writes "after it is durably
  persisted in one replica" ([regional disks](https://docs.cloud.google.com/compute/docs/disks/repd-failover)).
  PostgreSQL can't detect this.

Neither provider documents revoking `pg_logical_emit_message`. To settle all of this, run `checks.py` as the
application's role on each, with HA on. If a provider rejected the session `set_config`, pinned appends would fail
outright rather than silently.

[azure-ha]: https://learn.microsoft.com/en-us/azure/postgresql/high-availability/concepts-high-availability
