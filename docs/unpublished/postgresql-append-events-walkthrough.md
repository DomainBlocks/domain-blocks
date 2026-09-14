# Walking through `append_events`

Written by Claude (Fable 5.1) in a Claude Code session on 14 September 2026, at the request of the repository author.
Unpublished; it reflects the scripts as they were on that date.

A hands-on tour of `dbx.append_events`, one stage at a time, against a throwaway PostgreSQL container. Every query below
was run against `postgres:17` and the outputs shown are what it returned.

Tooling: Rider's built-in database tools are enough for everything here, and better than pgAdmin for it. pgAdmin's only
real advantage is its PL/pgSQL step debugger, which needs the `pldbgapi` extension that the stock `postgres:17` image
does not ship. It would also not help much: after validation and the lock, `append_events` is a single SQL statement, so
"stepping" means pulling that statement apart into its CTEs and running them one at a time, which is what this guide
does. psql inside the container is used for the one part Rider is awkward at, holding a transaction open in a second
session.

## 1. Set up

Start a server. `wal_level=logical` is only needed by the replication feed, but it matches the test container so the
same database works for the other parts of the store too.

```powershell
docker run --name dbx-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:17 -c wal_level=logical
```

Install the schema. The scripts contain a `__schema__` token that `SqlScripts.Load` replaces with the configured schema
name (default `dbx`, see `PostgresEventStoreOptions.Schema`); this does the same substitution and pipes the three
scripts through psql in one transaction, exactly as `PostgresEventStoreAdmin.InitializeAsync` does. Run it from the repo
root.

```powershell
$sqlDir = "src/DomainBlocks.EventStore.PostgreSQL/Sql"
$sql = ((Get-Content "$sqlDir/schema.sql", "$sqlDir/append_helpers.sql", "$sqlDir/append_events.sql" -Raw) -join "`n") -replace '__schema__', 'dbx'
$sql | docker exec -i dbx-pg psql -U postgres -v ON_ERROR_STOP=1 --single-transaction -q
```

To start over at any point, drop the schema and run the block above again:

```powershell
docker exec dbx-pg psql -U postgres -c "DROP SCHEMA dbx CASCADE"
```

To throw the server away: `docker rm -f dbx-pg`.

## 2. Connect Rider

1. Open the Database tool window (View > Tool Windows > Database), press `+`, choose Data Source > PostgreSQL.
2. Host `localhost`, port `5432`, user `postgres`, password `postgres`, database `postgres`. Download the driver if
   Rider offers to, then Test Connection.
3. On the Schemas tab tick `dbx`. Rider only introspects the current schema by default, so without this the objects stay
   invisible.
4. Right-click the data source > New > Query Console. `Ctrl+Enter` runs the statement under the caret; select a range to
   run several. Each statement gets its own result tab, and server messages (errors, `RAISE`, `LOG`) land in the Output
   tab of the console.

Useful in the console: `Ctrl+B` on any `dbx.` function name opens its source; right-click a statement > Explain Plan
(Analyze) shows a visual plan for any standalone statement, which works on the dry-run queries in steps 6 and 7.

## 3. Read the schema first

Expand `dbx` in the Database window. The three things `append_events` depends on:

- **`sequences`** has one row, `('event_log', 0)`. Its `next` column is the next global position. Every append locks
  this row `FOR UPDATE`, which is what serializes writers. Fill factor 50 keeps the hot row's updates on one page.
- **`event_log_commit_id_idx`** is unique and partial on `commit_index = 0`. Every request writes exactly one row with
  `commit_index = 0`, so this index has one entry per request, and probing it is the idempotency check.
- **`event_log_stream_id_stream_position_key`** is the unique `(stream_id, stream_position)` index. Read backwards with
  a limit it yields a stream's head in O (1). `stream_id` is `COLLATE "C"` so the comparison is byte-wise.

## 4. The running example

One batch is used throughout. Five requests, six events, chosen so every branch of the function fires:

| ord | stream    | expected kind  | version | commit id | events | what should happen                         |
|----:|-----------|----------------|--------:|-----------|-------:|--------------------------------------------|
|   1 | account-1 | 1 DoesNotExist |         | ...c1     |      2 | appended, stream created                   |
|   2 | account-2 | 0 Any          |         | ...c2     |      1 | appended                                   |
|   3 | account-1 | 3 AtVersion    |       1 | ...c3     |      1 | appended, sees the head left by request 1  |
|   4 | account-2 | 3 AtVersion    |       5 | ...c4     |      1 | conflict, head is 0                        |
|   5 | account-3 | 0 Any          |         | ...c2     |      1 | duplicate, commit id already used by ord 2 |

Codes, from the header of `append_events.sql`: expected kind 0 Any, 1 DoesNotExist, 2 Exists, 3 AtVersion. Status 0
Appended, 1 Conflict, 2 Duplicate. Observed kind 0 DoesNotExist, 1 AtVersion.

## 5. The helpers, one at a time

`append_events` does four things before its big statement: check isolation, validate, lock, probe commit ids. Steps A to
D exercise the helpers it uses, with literal arrays, so you see each piece's output on its own.

### A. `validate_append_batch`

Event arrays shorter than the event count:

```sql
SELECT dbx.validate_append_batch(
    ARRAY['account-1'], ARRAY[0]::smallint[], ARRAY[NULL]::bigint[],
    ARRAY['00000000-0000-0000-0000-0000000000c1']::uuid[], ARRAY[2],
    ARRAY['e1'], ARRAY['{}']::jsonb[], ARRAY[NULL]::bytea[], ARRAY[NULL]::jsonb[]);
-- ERROR:  event arrays must all have length 2, the sum of p_event_counts
```

AtVersion without a version:

```sql
SELECT dbx.validate_append_batch(
    ARRAY['account-1'], ARRAY[3]::smallint[], ARRAY[NULL]::bigint[],
    ARRAY['00000000-0000-0000-0000-0000000000c1']::uuid[], ARRAY[1],
    ARRAY['e1'], ARRAY['{}']::jsonb[], ARRAY[NULL]::bytea[], ARRAY[NULL]::jsonb[]);
-- ERROR:  request 0: expected version must be non-negative when expected kind is 3 (AtVersion)
```

Change the `NULL` version to `0` and it returns void. These are the only errors a well-formed caller can hit; every
other outcome is reported per request, not raised.

### B. `zip_requests`

Turns the parallel arrays into rows and derives the three columns the rest of the function keys on. Pass an empty
`existing_commits` array for now; the real call fills it from the index probe.

```sql
SELECT *
FROM dbx.zip_requests(
        ARRAY['account-1', 'account-2', 'account-1', 'account-2', 'account-3'],
        ARRAY[1, 0, 3, 3, 0]::smallint[],
        ARRAY[NULL, NULL, 1, 5, NULL]::bigint[],
        ARRAY['00000000-0000-0000-0000-0000000000c1',
              '00000000-0000-0000-0000-0000000000c2',
              '00000000-0000-0000-0000-0000000000c3',
              '00000000-0000-0000-0000-0000000000c4',
              '00000000-0000-0000-0000-0000000000c2']::uuid[],
        ARRAY[2, 1, 1, 1, 1],
        '{}'::uuid[])
ORDER BY ord;
```

```
 ord | stream_id | expected_kind | expected_version | commit_id | event_count | nth | duplicate | event_offset
-----+-----------+---------------+------------------+-----------+-------------+-----+-----------+--------------
   1 | account-1 |             1 |                  | ...c1     |           2 |   1 | f         |            1
   2 | account-2 |             0 |                  | ...c2     |           1 |   1 | f         |            3
   3 | account-1 |             3 |                1 | ...c3     |           1 |   2 | f         |            4
   4 | account-2 |             3 |                5 | ...c4     |           1 |   2 | f         |            5
   5 | account-3 |             0 |                  | ...c2     |           1 |   1 | t         |            6
```

What to look at:

- `nth` is the request's position among the requests to its own stream. Requests 3 and 4 are the second request to their
  streams, so `nth = 2`. This is what the recursive chain iterates over.
- `duplicate` is true for request 5 because `...c2` appeared earlier in the batch. The first occurrence wins.
- `event_offset` is the 1-based index of the request's first event in the flattened event arrays. Request 1 has two
  events, so request 2 starts at 3.

### C. `get_stream_heads`

The head of each distinct stream, or -1 for a stream with no events. On the empty log:

```sql
SELECT * FROM dbx.get_stream_heads(ARRAY['account-1', 'account-2', 'account-3']);
```

```
 stream_id | head
-----------+------
 account-2 |   -1
 account-1 |   -1
 account-3 |   -1
```

Run Explain Plan (Analyze) on this one. The lateral `max()` becomes `Index Only Scan Backward ... Limit 1` per stream,
which is why the probe does not get slower as streams grow.

### D. `get_append_status`

The whole decision in a truth table. Expected version fixed at 1:

```sql
SELECT k.kind, h.head,
       dbx.get_append_status(false, k.kind, 1, h.head) AS status,
       dbx.get_append_status(true, k.kind, 1, h.head)  AS status_if_duplicate
FROM (VALUES (0::smallint), (1::smallint), (2::smallint), (3::smallint)) AS k(kind)
         CROSS JOIN (VALUES (-1::bigint), (1::bigint), (3::bigint)) AS h(head)
ORDER BY k.kind, h.head;
```

```
 kind | head | status | status_if_duplicate
------+------+--------+---------------------
    0 |   -1 |      0 |                   2
    0 |    1 |      0 |                   2
    0 |    3 |      0 |                   2
    1 |   -1 |      0 |                   2
    1 |    1 |      1 |                   2
    1 |    3 |      1 |                   2
    2 |   -1 |      1 |                   2
    2 |    1 |      0 |                   2
    2 |    3 |      0 |                   2
    3 |   -1 |      1 |                   2
    3 |    1 |      0 |                   2
    3 |    3 |      1 |                   2
```

Duplicate wins over everything else. Any always appends. DoesNotExist needs head -1, Exists needs head >= 0, AtVersion
needs an exact match.

## 6. The recursive chain, as a dry run

The `chain` CTE is the heart of the function. Here it is lifted out of the function with the batch arrays in a
`batch` CTE standing in for the parameters. Nothing is written; you can run this as often as you like.

```sql
WITH RECURSIVE
    batch AS (SELECT ARRAY['account-1', 'account-2', 'account-1', 'account-2', 'account-3'] AS stream_ids,
                     ARRAY[1, 0, 3, 3, 0]::smallint[]                                       AS kinds,
                     ARRAY[NULL, NULL, 1, 5, NULL]::bigint[]                                AS versions,
                     ARRAY['00000000-0000-0000-0000-0000000000c1',
                           '00000000-0000-0000-0000-0000000000c2',
                           '00000000-0000-0000-0000-0000000000c3',
                           '00000000-0000-0000-0000-0000000000c4',
                           '00000000-0000-0000-0000-0000000000c2']::uuid[]                  AS commit_ids,
                     ARRAY[2, 1, 1, 1, 1]                                                   AS event_counts,
                     '{}'::uuid[]                                                           AS existing_commits),
    request AS (SELECT r.*
                FROM batch AS b
                         CROSS JOIN LATERAL dbx.zip_requests(b.stream_ids, b.kinds, b.versions, b.commit_ids,
                                                             b.event_counts, b.existing_commits) AS r),
    chain AS (SELECT h.stream_id, 0::bigint AS nth, NULL::bigint AS ord, NULL::bigint AS head_before,
                     NULL::smallint AS status, h.head AS head_after
              FROM batch AS b CROSS JOIN LATERAL dbx.get_stream_heads(b.stream_ids) AS h
              UNION ALL
              SELECT r.stream_id, r.nth, r.ord, c.head_after, d.status,
                     CASE WHEN d.status = 0 THEN c.head_after + r.event_count ELSE c.head_after END
              FROM chain AS c
                       JOIN request AS r ON r.stream_id = c.stream_id AND r.nth = c.nth + 1
                       CROSS JOIN LATERAL (SELECT dbx.get_append_status(r.duplicate, r.expected_kind, r.expected_version, c.head_after)) AS d(status))
SELECT * FROM chain ORDER BY stream_id, nth;
```

```
 stream_id | nth | ord | head_before | status | head_after
-----------+-----+-----+-------------+--------+------------
 account-1 |   0 |     |             |        |         -1
 account-1 |   1 |   1 |          -1 |      0 |          1
 account-1 |   2 |   3 |           1 |      0 |          2
 account-2 |   0 |     |             |        |         -1
 account-2 |   1 |   2 |          -1 |      0 |          0
 account-2 |   2 |   4 |           0 |      1 |          0
 account-3 |   0 |     |             |        |         -1
 account-3 |   1 |   5 |          -1 |      2 |         -1
```

How to read it:

- `nth = 0` rows are the seeds: one per distinct stream, holding the current head from `get_stream_heads`.
- Each recursion step joins the previous row of every stream to that stream's next request (`r.nth = c.nth + 1`) and
  decides it against `head_after` of the previous row. Iteration 1 decides ord 1, 2 and 5 together; iteration 2 decides
  ord 3 and 4. A batch with no repeated stream finishes in one iteration.
- `head_after` only advances on status 0. Request 3 sees head 1 (left by request 1's two events) and its AtVersion 1
  matches, so it appends. Request 4 sees head 0, wanted 5, conflict, and the head stays 0.
- Streams never interact, which is why evaluating them side by side gives the same result as evaluating the batch in
  order.

Explain Plan (Analyze) on this query shows a `Recursive Union` with a `WorkTable Scan`, and the `CASE` from
`get_append_status` inlined into the recursive term: the planner inlined the SQL helper, so it costs no function call.

## 7. `decided`: positions and the result rows

Extend the dry run with the `decided` CTE and the final projection. `position_start` is read from the sequence row,
which is what the function does under the lock.

```sql
WITH RECURSIVE
    batch AS (SELECT ARRAY['account-1', 'account-2', 'account-1', 'account-2', 'account-3'] AS stream_ids,
                     ARRAY[1, 0, 3, 3, 0]::smallint[]                                       AS kinds,
                     ARRAY[NULL, NULL, 1, 5, NULL]::bigint[]                                AS versions,
                     ARRAY['00000000-0000-0000-0000-0000000000c1',
                           '00000000-0000-0000-0000-0000000000c2',
                           '00000000-0000-0000-0000-0000000000c3',
                           '00000000-0000-0000-0000-0000000000c4',
                           '00000000-0000-0000-0000-0000000000c2']::uuid[]                  AS commit_ids,
                     ARRAY[2, 1, 1, 1, 1]                                                   AS event_counts,
                     '{}'::uuid[]                                                           AS existing_commits,
                     (SELECT s.next FROM dbx.sequences AS s WHERE s.name = 'event_log')     AS position_start),
    request AS (SELECT r.*
                FROM batch AS b
                         CROSS JOIN LATERAL dbx.zip_requests(b.stream_ids, b.kinds, b.versions, b.commit_ids,
                                                             b.event_counts, b.existing_commits) AS r),
    chain AS (SELECT h.stream_id, 0::bigint AS nth, NULL::bigint AS ord, NULL::bigint AS head_before,
                     NULL::smallint AS status, h.head AS head_after
              FROM batch AS b CROSS JOIN LATERAL dbx.get_stream_heads(b.stream_ids) AS h
              UNION ALL
              SELECT r.stream_id, r.nth, r.ord, c.head_after, d.status,
                     CASE WHEN d.status = 0 THEN c.head_after + r.event_count ELSE c.head_after END
              FROM chain AS c
                       JOIN request AS r ON r.stream_id = c.stream_id AND r.nth = c.nth + 1
                       CROSS JOIN LATERAL (SELECT dbx.get_append_status(r.duplicate, r.expected_kind, r.expected_version, c.head_after)) AS d(status)),
    decided AS (SELECT r.ord, r.stream_id, r.commit_id, r.event_count, r.event_offset, c.status, c.head_before,
                       b.position_start +
                       coalesce(sum(r.event_count) FILTER (WHERE c.status = 0)
                                OVER (ORDER BY r.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS first_pos
                FROM chain AS c
                         JOIN request AS r ON r.ord = c.ord
                         CROSS JOIN batch AS b)
SELECT d.*,
       (d.ord - 1)::integer                                            AS request_index,
       CASE WHEN d.head_before < 0 THEN 0 ELSE 1 END                   AS observed_kind,
       nullif(d.head_before, -1)                                       AS observed_version,
       CASE WHEN d.status = 0 THEN d.first_pos END                     AS first_position,
       CASE WHEN d.status = 0 THEN d.first_pos + d.event_count - 1 END AS last_position
FROM decided AS d
ORDER BY d.ord;
```

```
 ord | stream_id | event_count | event_offset | status | head_before | first_pos | request_index | observed_kind | observed_version | first_position | last_position
-----+-----------+-------------+--------------+--------+-------------+-----------+---------------+---------------+------------------+----------------+---------------
   1 | account-1 |           2 |            1 |      0 |          -1 |         0 |             0 |             0 |                  |              0 |             1
   2 | account-2 |           1 |            3 |      0 |          -1 |         2 |             1 |             0 |                  |              2 |             2
   3 | account-1 |           1 |            4 |      0 |           1 |         3 |             2 |             1 |                1 |              3 |             3
   4 | account-2 |           1 |            5 |      1 |           0 |         4 |             3 |             1 |                0 |                |
   5 | account-3 |           1 |            6 |      2 |          -1 |         4 |             4 |             0 |                  |                |
```

- `first_pos` is a running sum of event counts over appended requests only, in batch order. Requests 4 and 5 still get a
  `first_pos` of 4 but it is unused, and the next batch will start at 4: no gaps.
- The insert CTE (not reproduced here because it writes) takes each row with status 0, generates `k = 0 ..
  event_count - 1`, and joins the flattened event arrays at `event_offset + k`. Global position is `first_pos + k`,
  stream position is `head_before + 1 + k`, commit index is `k`.
- The result columns report `head_before`, the head the caller would have observed, not the head after.

## 8. The real thing

Call the function with the same batch plus its six events:

```sql
SELECT *
FROM dbx.append_events(
        ARRAY['account-1', 'account-2', 'account-1', 'account-2', 'account-3'],
        ARRAY[1, 0, 3, 3, 0]::smallint[],
        ARRAY[NULL, NULL, 1, 5, NULL]::bigint[],
        ARRAY['00000000-0000-0000-0000-0000000000c1',
              '00000000-0000-0000-0000-0000000000c2',
              '00000000-0000-0000-0000-0000000000c3',
              '00000000-0000-0000-0000-0000000000c4',
              '00000000-0000-0000-0000-0000000000c2']::uuid[],
        ARRAY[2, 1, 1, 1, 1],
        ARRAY['Opened', 'Deposited', 'Opened', 'Withdrawn', 'Closed', 'Opened'],
        ARRAY['{"amount":0}', '{"amount":10}', '{"amount":0}', '{"amount":5}', '{}', '{}']::jsonb[],
        ARRAY[NULL, NULL, NULL, NULL, NULL, NULL]::bytea[],
        ARRAY[NULL, NULL, NULL, NULL, NULL, NULL]::jsonb[]);
```

```
 request_index | status | observed_kind | observed_version | first_position | last_position
---------------+--------+---------------+------------------+----------------+---------------
             0 |      0 |             0 |                  |              0 |             1
             1 |      0 |             0 |                  |              2 |             2
             2 |      0 |             1 |                1 |              3 |             3
             3 |      1 |             1 |                0 |                |
             4 |      2 |             0 |                  |                |
```

Identical to the dry run. Now look at what landed:

```sql
SELECT position, stream_id, stream_position, commit_id, commit_index, event_name, event_data, created_at
FROM dbx.event_log ORDER BY position;
SELECT * FROM dbx.sequences;
```

```
 position | stream_id | stream_position | commit_id | commit_index | event_name | event_data
----------+-----------+-----------------+-----------+--------------+------------+----------------
        0 | account-1 |               0 | ...c1     |            0 | Opened     | {"amount": 0}
        1 | account-1 |               1 | ...c1     |            1 | Deposited  | {"amount": 10}
        2 | account-2 |               0 | ...c2     |            0 | Opened     | {"amount": 0}
        3 | account-1 |               2 | ...c3     |            0 | Withdrawn  | {"amount": 5}
```

Four rows, `sequences.next` is 4, all four share one `created_at` (taken once after the lock). Events 5 and 6 of the
input (`Closed`, `Opened`) were never inserted because their requests were rejected.

**Replay the exact same call.** Every commit id is now in the partial index, so `v_existing_commits` holds them all and
everything reports status 2, except request 4 which is still a conflict (it never got a `commit_index = 0` row).
`sequences.next` stays 4. Notice `observed_version` now reports the current heads (2 and 0): a duplicate is decided
against the live head, it just does not write.

**Conflict then success on one stream.** Two AtVersion requests to `account-1`, the first wrong, the second right:

```sql
SELECT *
FROM dbx.append_events(
        ARRAY['account-1', 'account-1'],
        ARRAY[3, 3]::smallint[],
        ARRAY[7, 2]::bigint[],
        ARRAY['00000000-0000-0000-0000-0000000000d1', '00000000-0000-0000-0000-0000000000d2']::uuid[],
        ARRAY[1, 1],
        ARRAY['Deposited', 'Deposited'],
        ARRAY['{"amount":1}', '{"amount":2}']::jsonb[],
        ARRAY[NULL, NULL]::bytea[],
        ARRAY[NULL, NULL]::jsonb[]);
```

```
 request_index | status | observed_kind | observed_version | first_position | last_position
---------------+--------+---------------+------------------+----------------+---------------
             0 |      1 |             1 |                2 |                |
             1 |      0 |             1 |                2 |              4 |             4
```

The conflict did not advance the head, so the second request still sees version 2 and appends at position 4.

**Wrong isolation level.** The function refuses anything but READ COMMITTED, because the blocking `FOR UPDATE` must
re-read the row another writer just committed:

```sql
BEGIN ISOLATION LEVEL REPEATABLE READ;
SELECT * FROM dbx.append_events(ARRAY['x'], ARRAY[0]::smallint[], ARRAY[NULL]::bigint[],
    ARRAY['00000000-0000-0000-0000-0000000000e1']::uuid[], ARRAY[1],
    ARRAY['e'], ARRAY['{}']::jsonb[], ARRAY[NULL]::bytea[], ARRAY[NULL]::jsonb[]);
-- ERROR:  append_events requires READ COMMITTED isolation (current: repeatable read)
ROLLBACK;
```

## 9. Watch the lock

This needs two sessions. Use psql in a terminal for the one that holds the transaction open, and the Rider console for
the rest.

Terminal, session A. Everything in one `-c` runs as one transaction; the sleep keeps it open for 30 seconds:

```powershell
docker exec dbx-pg psql -U postgres -c "BEGIN; SELECT * FROM dbx.append_events(ARRAY['lock-demo'], ARRAY[0]::smallint[], ARRAY[NULL]::bigint[], ARRAY['00000000-0000-0000-0000-0000000000a1']::uuid[], ARRAY[1], ARRAY['e'], ARRAY['{}']::jsonb[], ARRAY[NULL]::bytea[], ARRAY[NULL]::jsonb[]); SELECT pg_sleep(30); COMMIT;"
```

Rider console, session B, straight away. It hangs until A commits:

```sql
SELECT * FROM dbx.append_events(ARRAY['lock-demo'], ARRAY[0]::smallint[], ARRAY[NULL]::bigint[],
    ARRAY['00000000-0000-0000-0000-0000000000a2']::uuid[], ARRAY[1],
    ARRAY['e'], ARRAY['{}']::jsonb[], ARRAY[NULL]::bytea[], ARRAY[NULL]::jsonb[]);
```

Second terminal (or a second Rider console with a new session), while B is waiting:

```sql
SELECT pid, state, wait_event_type, wait_event, pg_blocking_pids(pid) AS blocked_by, left(query, 40) AS query
FROM pg_stat_activity
WHERE datname = 'postgres' AND pid <> pg_backend_pid() AND state <> 'idle';

SELECT l.pid, l.locktype, l.mode, l.granted, l.relation::regclass
FROM pg_locks AS l
WHERE l.locktype IN ('tuple', 'transactionid') OR l.relation = 'dbx.sequences'::regclass
ORDER BY pid;
```

```
 pid | state  | wait_event_type |  wait_event   | blocked_by | query
-----+--------+-----------------+---------------+------------+------------------------------------------
 105 | active | Timeout         | PgSleep       | {}         | BEGIN; SELECT * FROM dbx.append_events(A
 112 | active | Lock            | transactionid | {105}      | SELECT * FROM dbx.append_events(ARRAY['l

 pid |   locktype    |        mode         | granted |   relation
-----+---------------+---------------------+---------+---------------
 105 | relation      | RowShareLock        | t       | dbx.sequences
 105 | relation      | RowExclusiveLock    | t       | dbx.sequences
 105 | transactionid | ExclusiveLock       | t       |
 112 | relation      | RowShareLock        | t       | dbx.sequences
 112 | transactionid | ShareLock           | f       |
 112 | tuple         | AccessExclusiveLock | t       | dbx.sequences
```

B holds the tuple lock on the sequence row and is waiting on A's transaction id, which is how row locks wait in
PostgreSQL. When A commits, B's `FOR UPDATE` re-reads the row (READ COMMITTED) and gets the `next` A left behind. B's
result shows `observed_version 0` and a position one past A's: it saw the row A wrote, even though its own statement
began before A committed. That re-read is the whole reason for the isolation check in step 8.

## 10. See the plan the function actually runs

Rider's Explain Plan works on the dry runs in steps 6 and 7, but not inside the function. `auto_explain` with nested
statements enabled logs the plan of every statement the function executes, and `client_min_messages = log` sends those
log lines to the client, where Rider shows them in the console's Output tab. Run all of this in one console:

```sql
LOAD 'auto_explain';
SET auto_explain.log_min_duration = 0;
SET auto_explain.log_nested_statements = on;
SET auto_explain.log_analyze = on;
SET auto_explain.log_verbose = on;
SET client_min_messages = log;

SELECT * FROM dbx.append_events(ARRAY['account-9'], ARRAY[0]::smallint[], ARRAY[NULL]::bigint[],
    ARRAY['00000000-0000-0000-0000-0000000000e9']::uuid[], ARRAY[1],
    ARRAY['e'], ARRAY['{}']::jsonb[], ARRAY[NULL]::bytea[], ARRAY[NULL]::jsonb[]);
```

You get five plans, in execution order, which is the clearest picture of the function's fixed statement count:

1. The aggregate inside `validate_append_batch` (a `Function Scan` over `unnest($1..$5)`).
2. `SELECT ... FOR UPDATE` on `sequences`: `LockRows` over an index scan on `sequences_pkey`.
3. The commit id probe: `Index Only Scan using event_log_commit_id_idx`, `Heap Fetches: 0`.
4. The big statement. Things worth finding in it:
    - `CTE chain` > `Recursive Union` with a `WorkTable Scan`; the recursive term is a hash join on
      `(stream_id, nth + 1)` and the status `CASE` is inlined, no call to `get_append_status`.
    - Inside the seed: `Index Only Scan Backward using event_log_stream_id_stream_position_key` under a `Limit`, which
      is `get_stream_heads` inlined.
    - `CTE inserted` > `Insert on dbx.event_log` fed by `generate_series` joined to `unnest($6..$9)` by ordinal.
    - `CTE advanced` > `Update on dbx.sequences` with a `One-Time Filter` on the inserted count.
    - Parameters show as `$1`, `$19`, and so on because of `plan_cache_mode = force_generic_plan`: this is the generic
      plan reused across calls.
5. The outer `Function Scan on dbx.append_events` with its total duration.

If the Output tab does not show the LOG lines, run the same block through psql: `docker exec -it dbx-pg psql -U
postgres` and paste it.

## 11. Back to the C# side

- `tests/.../AppendFunctionClient.cs` builds the nine array parameters from `Request` records and calls the function by
  name. `AppendFunctionTests.cs` exercises the same cases as this guide, and is the place to add a test if you find an
  edge while exploring.
- `BatchingAppender.cs` queues append requests on a channel and drains them in batches; `AppendBatchCommand.cs` turns
  one batch into one call of the function, in autocommit mode so the sequence row lock is released the moment the batch
  commits. Batching is why the function takes request arrays rather than one request per call.
- `PostgresEventStoreAdmin.InitializeAsync` runs the three scripts in one transaction under an advisory lock, the same
  thing the PowerShell block in step 1 does by hand.
