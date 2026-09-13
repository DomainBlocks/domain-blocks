# `append_events.sql` explained

A line-by-line walkthrough of `src/DomainBlocks.EventStore.PostgreSQL/Sql/append_events.sql`, the PL/pgSQL function
that commits a batch of append requests to the PostgreSQL event store. It is written to be read alongside the source
and is intended as the baseline for later optimisation work: it describes what each statement does, why it is there,
and what it costs.

Documentation links point at the PostgreSQL 17 manual, which is the version the test suite runs against.

Contents

1. [How the function is used](#1-how-the-function-is-used)
2. [The tables it touches](#2-the-tables-it-touches)
3. [The contract: arrays and codes](#3-the-contract-arrays-and-codes)
4. [Mental model](#4-mental-model)
5. [Line-by-line walkthrough](#5-line-by-line-walkthrough)
6. [Worked example](#6-worked-example)
7. [Correctness properties and why they hold](#7-correctness-properties-and-why-they-hold)
8. [Cost model: where the time goes](#8-cost-model-where-the-time-goes)

---

## 1. How the function is used

`AppendBatchCommand` (in `AppendBatchCommand.cs`) issues a single statement per batch:

```sql
SELECT request_index, status, observed_kind, observed_version
FROM <schema>.append_events($1, $2, $3, $4, $5, $6, $7, $8, $9)
```

Key facts about the call site that the SQL relies on:

- **Autocommit.** The command is not enlisted in a client transaction, so the function call *is* the transaction. The
  row lock taken inside the function is released the instant the statement completes, which is what lets the next
  batch proceed. See [Transactions](https://www.postgresql.org/docs/17/tutorial-transactions.html): a statement outside
  an explicit `BEGIN` runs in its own implicit transaction.
- **One appender loop per store.** `BatchingAppender` drains a channel and calls the command from a single loop, so a
  given process only ever has one batch in flight. Serialisation across *processes* is done by the SQL, not the client.
- **Retry on transient failure.** The same batch is re-executed once if Npgsql classifies the failure as transient.
  That is only safe because the function treats already-committed commit ids as duplicates (section 7).
- **The result set is the whole protocol.** The client expects exactly one row per request and completes each request's
  `Task` from its row. `first_position` and `last_position` are returned but not currently read by the client.
- **`__schema__` is replaced at load time** by `SqlScripts.Load`, and the function is created by
  `PostgresEventStoreAdmin` during schema initialisation.

## 2. The tables it touches

From `schema.sql`:

```sql
CREATE TABLE __schema__.event_log (
    position         bigint      NOT NULL,   -- global, gap-free, commit-ordered
    stream_id        text        NOT NULL,
    stream_position  bigint      NOT NULL,   -- 0-based version within the stream
    commit_id        uuid        NOT NULL,   -- idempotency key, one per request
    commit_index     integer     NOT NULL,   -- 0-based index of the event within its request
    event_name       text        NOT NULL,
    event_data       jsonb,
    event_data_bytes bytea,
    metadata         jsonb,
    created_at       timestamptz NOT NULL,
    PRIMARY KEY (position),
    UNIQUE (stream_id, stream_position),
    CHECK ((event_data IS NULL) <> (event_data_bytes IS NULL)),
    ...
);
CREATE INDEX event_log_commit_id_idx ON __schema__.event_log (commit_id) WHERE commit_index = 0;

CREATE TABLE __schema__.sequences (name text PRIMARY KEY, next bigint NOT NULL) WITH (fillfactor = 50);
INSERT INTO __schema__.sequences VALUES ('event_log', 0) ON CONFLICT DO NOTHING;
```

Three indexes matter to the function:

| Index | Used by |
|---|---|
| `event_log_pkey (position)` | The final `INSERT` (uniqueness check on every new row). |
| `event_log_stream_id_stream_position_key (stream_id, stream_position)` | The head prefetch (one backwards probe per distinct stream) and the `INSERT` uniqueness check. |
| `event_log_commit_id_idx (commit_id) WHERE commit_index = 0` | The idempotency probe. Partial: one entry per request rather than per event, since every request writes exactly one row with `commit_index = 0`. |

The `sequences` table holds a single hot row. Its low `fillfactor` leaves free space on the page so that every
`UPDATE` can be a HOT update that never has to touch an index. See
[Heap-Only Tuples](https://www.postgresql.org/docs/17/storage-hot.html) and the
[`fillfactor` storage parameter](https://www.postgresql.org/docs/17/sql-createtable.html#RELOPTION-FILLFACTOR).

Note that this is a plain table row, **not** a PostgreSQL `SEQUENCE`. A real sequence is non-transactional (values
are consumed even if the transaction rolls back) and would produce gaps; a row locked `FOR UPDATE` is transactional and
gap-free at the cost of serialising writers. See [`nextval` caveats](https://www.postgresql.org/docs/17/functions-sequence.html).

## 3. The contract: arrays and codes

The function takes nine arrays in two groups. This is the "structure of arrays" encoding that lets a batch of any size
be sent as a fixed number of parameters in one round trip.

**Request arrays** (all of length R, one element per request):

| Parameter | Type | Meaning |
|---|---|---|
| `p_stream_ids` | `text[]` | Target stream. |
| `p_expected_kinds` | `smallint[]` | 0 Any, 1 DoesNotExist, 2 Exists, 3 AtVersion. |
| `p_expected_versions` | `bigint[]` | Only non-null when kind = 3. May contain SQL `NULL`s. |
| `p_commit_ids` | `uuid[]` | Idempotency key for the request. |
| `p_event_counts` | `integer[]` | How many of the event-array elements belong to this request. |

**Event arrays** (all of length E = sum of `p_event_counts`, flattened in request order):

| Parameter | Type | Meaning |
|---|---|---|
| `p_event_names` | `text[]` | |
| `p_event_data` | `jsonb[]` | JSON payload, or `NULL` when bytes are used. |
| `p_event_data_bytes` | `bytea[]` | Binary payload, or `NULL` when JSON is used. |
| `p_metadata` | `jsonb[]` | Optional. |

**Result rows** (one per request, in request order):

| Column | Meaning |
|---|---|
| `request_index` | 0-based index into the batch (the client uses it to find the request). |
| `status` | 0 Appended, 1 Conflict, 2 Duplicate. |
| `observed_kind` | 0 DoesNotExist, 1 AtVersion. Populated for every row; only meaningful on conflict. |
| `observed_version` | The stream head the request saw, or `NULL` if the stream did not exist. |
| `first_position` / `last_position` | Global positions of the appended events; `NULL` unless appended. |

The mirror of these codes on the client side is `AppendProtocol.cs`.

## 4. Mental model

The batch is committed with a fixed number of SQL statements regardless of how many requests or events it contains.
Everything per-request happens in PL/pgSQL memory between two table reads and two table writes:

```
 1. validate arguments             (no table access; raise => whole batch fails)
 2. SELECT ... FOR UPDATE          lock the sequence row  ──────────┐  serialises all appenders,
 3. probe existing commit ids      1 index scan                     │  held until COMMIT
 4. prefetch stream heads          1 index probe per distinct stream│
 5. FOR loop over requests         pure memory: decide, assign      │
 6. INSERT accepted events         1 statement, E' rows             │
 7. UPDATE sequence row            1 statement                      │
 8. RETURN result set              ──────────────────────────── COMMIT
```

Steps 3 and 4 read the *committed* state that exists at the time the lock is acquired. Because every other appender is
blocked on the same lock, that state cannot change until this batch commits, so the in-memory pass in step 5 can
reason about it without re-checking.

## 5. Line-by-line walkthrough

### Lines 1–18: header comment

The comment is the design summary. Three claims in it are worth pinning to the code:

- "All appenders serialize on the event_log sequence row, whose lock is held until commit" → lines 113–117 and the
  autocommit call site.
- "Each request is evaluated independently ... Only protocol violations raise" → the validation block (lines 71–111)
  raises; the request loop (lines 144–202) never does.
- "fixed number of statements regardless of its size" → the four table statements at lines 114, 128, 135 and 210/233.

### Lines 19–28: signature

```sql
CREATE OR REPLACE FUNCTION __schema__.append_events(
    p_stream_ids        text[],
    ...
    p_metadata          jsonb[])
```

`CREATE OR REPLACE` lets the schema script be re-run to upgrade the function body without dropping it. The argument
list (names and types) must stay the same for a replace to succeed; changing it requires `DROP FUNCTION` first.
See [CREATE FUNCTION](https://www.postgresql.org/docs/17/sql-createfunction.html).

All parameters are one-dimensional arrays. See [Arrays](https://www.postgresql.org/docs/17/arrays.html). Npgsql sends
them in binary form, so there is no text parsing on the server.

### Lines 29–35: `RETURNS TABLE`

```sql
RETURNS TABLE (
    request_index    integer,
    status           smallint,
    observed_kind    smallint,
    observed_version bigint,
    first_position   bigint,
    last_position    bigint)
```

This declares a set-returning function. In PL/pgSQL the six output columns double as **local variables**: assigning to
`status` sets the value that the next `RETURN NEXT` will emit. That is why the request loop assigns to `request_index`,
`status` and so on directly rather than to `DECLARE`d variables.
See [Returning from a function: `RETURN NEXT`](https://www.postgresql.org/docs/17/plpgsql-control-structures.html#PLPGSQL-STATEMENTS-RETURNING-RETURN-NEXT).

Important implementation detail from that page: PL/pgSQL does **not** stream rows to the caller. `RETURN NEXT`
appends to a tuplestore, and the whole set is handed back when the function finishes. So the client sees no rows
until after the `INSERT`, the `UPDATE` and the commit, which is exactly what the completion semantics need.

### Line 36: `LANGUAGE plpgsql`

The body is procedural. PL/pgSQL is interpreted; each embedded SQL statement is prepared once per session and cached,
and expressions such as `v_position + v_req.event_count` are themselves tiny `SELECT`s executed through the SQL engine
(with a fast path for simple expressions). See
[PL/pgSQL under the hood](https://www.postgresql.org/docs/17/plpgsql-implementation.html).

### Line 37: `SET plan_cache_mode = force_generic_plan`

```sql
SET plan_cache_mode = force_generic_plan
```

A `SET` clause on `CREATE FUNCTION` applies the setting for the duration of each call and restores it on exit
([CREATE FUNCTION, `configuration_parameter`](https://www.postgresql.org/docs/17/sql-createfunction.html)).

Why it matters here: every SQL statement inside a PL/pgSQL function is a prepared statement whose PL/pgSQL variables
become parameters (`$1`, `$2` ...). For a prepared statement the planner normally builds a *custom plan* using the
actual parameter values for the first five executions, then compares against a *generic plan* and may switch. With
`force_generic_plan` it always uses the generic plan, so:

- there is no per-call planning cost after the first execution in a session;
- the plan cannot flip between shapes depending on array sizes, which keeps latency predictable;
- the trade-off is that the planner cannot use array cardinality to pick, say, a different join order for a very large
  batch.

See [`plan_cache_mode`](https://www.postgresql.org/docs/17/runtime-config-query.html#GUC-PLAN-CACHE-MODE) and
[Plan caching in PL/pgSQL](https://www.postgresql.org/docs/17/plpgsql-implementation.html#PLPGSQL-PLAN-CACHING).
`PREPARE` explains the custom/generic choice in detail:
[PREPARE, Notes](https://www.postgresql.org/docs/17/sql-prepare.html#SQL-PREPARE-NOTES).

### Line 38 and 237: `AS $fn$ ... $fn$`

Dollar quoting delimits the function body so that ordinary single quotes inside it need no escaping.
See [Dollar-quoted string constants](https://www.postgresql.org/docs/17/sql-syntax-lexical.html#SQL-SYNTAX-DOLLAR-QUOTING).

### Lines 39–61: `DECLARE`

See [Declarations](https://www.postgresql.org/docs/17/plpgsql-declarations.html).

| Variable | Purpose |
|---|---|
| `c_sequence_name CONSTANT text := 'event_log'` | Key of the row in `sequences`. `CONSTANT` makes assignment a compile error. |
| `v_request_count integer` | R. |
| `v_event_total bigint` | E, the sum of `p_event_counts`. `bigint` because `sum(integer)` returns `bigint`. |
| `v_position_start bigint` | The value read from the sequence row: the global position the first accepted event will get. |
| `v_position bigint` | Running cursor; advanced by each accepted request's event count. |
| `v_created_at timestamptz` | One timestamp stamped on every event in the batch. |
| `v_existing_commits uuid[]` | Commit ids from the batch that are already in `event_log`. |
| `v_heads bigint[]` | Current head `stream_position` of each *distinct* stream in the batch, `-1` if the stream has no events. Indexed by the stream's rank in `stream_id` order (see line 147). |
| `v_head bigint` | The head of the stream for the request currently being evaluated. |
| `v_matches boolean` | Whether the request's expectation holds against `v_head`. |
| `v_accepted integer := 0` | Count of accepted requests so far, and the write index into the `v_acc_*` arrays. |
| `v_acc_index integer[] := '{}'` | 1-based index into the request arrays of each accepted request. |
| `v_acc_first_pos bigint[]` | Global position of each accepted request's first event. |
| `v_acc_first_ver bigint[]` | Stream position of each accepted request's first event. |
| `v_acc_event_offset bigint[]` | 1-based offset into the event arrays where the request's events start. |
| `v_acc_event_count integer[]` | Copy of the request's event count. |
| `v_req record` | Loop variable; its shape is whatever the `FOR` query returns. See [Record types](https://www.postgresql.org/docs/17/plpgsql-declarations.html#PLPGSQL-DECLARATION-RECORDS). |

The five `v_acc_*` arrays are the "accepted list" in structure-of-arrays form. They exist so that the `INSERT` at
line 210 can be a single set-based statement over `unnest(...)` rather than one `INSERT` per request.

### Lines 63–69: isolation-level guard

```sql
IF current_setting('transaction_isolation') <> 'read committed' THEN
    RAISE EXCEPTION 'append_events requires READ COMMITTED isolation (current: %)', ...
        USING ERRCODE = 'invalid_transaction_state';
END IF;
```

`current_setting` reads a run-time parameter as text ([Configuration settings functions](https://www.postgresql.org/docs/17/functions-admin.html#FUNCTIONS-ADMIN-SET)).
`transaction_isolation` reports the isolation level of the current transaction
([`transaction_isolation`](https://www.postgresql.org/docs/17/runtime-config-client.html#GUC-TRANSACTION-ISOLATION)).

Why the guard exists: the `SELECT ... FOR UPDATE` at line 114 may block behind another batch. When that batch commits,
what happens next depends on the isolation level:

- Under **READ COMMITTED**, the blocked statement wakes up, re-reads the *newly committed* version of the row and
  locks that. This is the behaviour the design relies on: the next batch sees the updated `next` value.
- Under **REPEATABLE READ** or **SERIALIZABLE**, the row has changed since the transaction's snapshot, so the statement
  fails with `could not serialize access due to concurrent update` instead.

Both behaviours are described in [Transaction Isolation](https://www.postgresql.org/docs/17/transaction-iso.html)
(see the paragraph on `UPDATE`, `DELETE`, `SELECT FOR UPDATE` waiting for a concurrent updater in
[Read Committed](https://www.postgresql.org/docs/17/transaction-iso.html#XACT-READ-COMMITTED) and
the corresponding paragraph in [Repeatable Read](https://www.postgresql.org/docs/17/transaction-iso.html#XACT-REPEATABLE-READ)).

Failing fast with a clear message is preferable to intermittent serialisation failures under load. READ COMMITTED is
the PostgreSQL default, so this only fires if `default_transaction_isolation` has been changed on the server or in the
connection string.

`RAISE EXCEPTION ... USING ERRCODE` is documented in
[Errors and Messages](https://www.postgresql.org/docs/17/plpgsql-errors-and-messages.html); the condition names come
from [Appendix A. Error Codes](https://www.postgresql.org/docs/17/errcodes-appendix.html). An unhandled exception
aborts the function and, because the call is autocommit, rolls back the entire statement. No partial batch is ever
visible.

### Lines 71–75: request count and the empty batch

```sql
v_request_count := coalesce(cardinality(p_stream_ids), 0);

IF v_request_count = 0 THEN
    RETURN;
END IF;
```

`cardinality` returns the total element count of an array, and returns `NULL` for a `NULL` array; `coalesce` turns
that into 0 so a `NULL` argument is treated as an empty batch. See
[`cardinality`](https://www.postgresql.org/docs/17/functions-array.html) and
[`COALESCE`](https://www.postgresql.org/docs/17/functions-conditional.html#FUNCTIONS-COALESCE-NVL-IFNULL).

A bare `RETURN` in a set-returning function ends execution and returns whatever has been accumulated: here, nothing.
The lock is never taken for an empty batch. (The client never sends one, but the function is defensive.)

### Lines 77–83: request arrays must agree

```sql
IF coalesce(cardinality(p_expected_kinds), 0) <> v_request_count OR
   ... THEN
    RAISE EXCEPTION 'request arrays must all have length %', v_request_count
        USING ERRCODE = 'invalid_parameter_value';
END IF;
```

Structure-of-arrays encoding is only meaningful if the parallel arrays line up. Note `cardinality` counts `NULL`
elements, so `p_expected_versions` (which legitimately contains `NULL`s for non-`AtVersion` requests) still has length R.

### Lines 85–88: event counts must be positive

```sql
IF EXISTS (SELECT 1 FROM unnest(p_event_counts) AS c WHERE c IS NULL OR c <= 0) THEN
    RAISE EXCEPTION 'event counts must be positive' ...
```

`unnest` expands an array into rows so it can be filtered with ordinary SQL
([`unnest`](https://www.postgresql.org/docs/17/functions-array.html)); `EXISTS` stops at the first offending row
([`EXISTS`](https://www.postgresql.org/docs/17/functions-subquery.html#FUNCTIONS-SUBQUERY-EXISTS)).

A zero count would make a request occupy no events yet still "append", which would break the position arithmetic
(`last_position = first_position + count - 1` would be less than `first_position`). A `NULL` would poison the `sum` on
line 90.

### Line 90: total event count

```sql
SELECT sum(c) INTO v_event_total FROM unnest(p_event_counts) AS c;
```

`SELECT ... INTO` a PL/pgSQL variable is described in
[Executing a query with a single-row result](https://www.postgresql.org/docs/17/plpgsql-statements.html#PLPGSQL-STATEMENTS-SQL-ONEROW).
This is a full pass over the R-element array, but it happens in SQL, not in the interpreter loop.

### Lines 92–98: event arrays must agree

Same check as lines 77–83 but against E. If the client's flattening were ever wrong, the `JOIN ... ON ev.ord =
a.event_offset + g.k` in the final `INSERT` would silently drop rows or attach the wrong payloads, so this is
caught up front.

### Lines 100–111: per-request semantic validation

```sql
IF EXISTS (
    SELECT 1
    FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids)
        AS r(stream_id, kind, version, commit_id)
    WHERE r.stream_id IS NULL OR r.stream_id = ''
       OR r.commit_id IS NULL
       OR r.kind IS NULL OR r.kind NOT BETWEEN 0 AND 3
       OR (r.kind = 3 AND (r.version IS NULL OR r.version < 0))
       OR (r.kind <> 3 AND r.version IS NOT NULL)) THEN
    RAISE EXCEPTION 'invalid request: ...' USING ERRCODE = 'invalid_parameter_value';
END IF;
```

Multi-argument `unnest` zips several arrays into one row set, padding shorter ones with `NULL`; because the lengths
were just checked, every row here is one complete request. This form is only allowed in the `FROM` clause. See
[Table functions](https://www.postgresql.org/docs/17/queries-table-expressions.html#QUERIES-TABLEFUNCTIONS).

The rules enforced:

| Rule | Reason |
|---|---|
| `stream_id` not null or empty | Mirrors the `CHECK (stream_id <> '')` on `event_log`; failing here is a clean protocol error rather than a constraint violation mid-insert. |
| `commit_id` not null | It is the idempotency key and an `= ANY` test against `NULL` would never match. |
| `kind` in 0..3 | Any other value would make the `CASE` at line 175 yield `NULL`, and `IF NOT NULL` is treated as false, which would silently *reject* the request as a conflict. Validating up front keeps that path unreachable. |
| kind 3 ⇒ version present and ≥ 0 | `AtVersion` needs a version; stream positions are 0-based. |
| kind ≠ 3 ⇒ version null | Catches a client that sends a version with the wrong kind. |

### Lines 113–122: the lock

```sql
SELECT s.next INTO v_position_start
FROM __schema__.sequences AS s
WHERE s.name = c_sequence_name
FOR UPDATE;

IF NOT FOUND THEN
    RAISE EXCEPTION 'sequence row "%" not found; ...' USING ERRCODE = 'undefined_object';
END IF;
```

This is the heart of the concurrency design.

`FOR UPDATE` takes a row-level lock on the `event_log` row of `sequences`
([`FOR UPDATE`](https://www.postgresql.org/docs/17/sql-select.html#SQL-FOR-UPDATE-SHARE),
[Row-Level Locks](https://www.postgresql.org/docs/17/explicit-locking.html#LOCKING-ROWS)). Row locks are held until
the transaction ends; there is no way to release one earlier. Because the call is autocommit, "transaction end" is the
end of this function call.

The consequence is a queue: every concurrent `append_events` call from any connection blocks at this line until the
one holding the lock commits or rolls back. When it wakes, READ COMMITTED semantics make it re-read the row and pick up
the `next` value the previous batch wrote at line 233. Hence:

- global positions are handed out in commit order;
- there are no gaps, because a rolled-back batch never updated the row;
- throughput of the whole store is bounded by the critical section between this line and commit, which is why the
  client batches.

`FOUND` is set by `SELECT INTO` and is false when no row matched
([Obtaining the result status](https://www.postgresql.org/docs/17/plpgsql-statements.html#PLPGSQL-STATEMENTS-DIAGNOSTICS)).
A missing row means `schema.sql` was never run.

### Lines 124–125: cursor and timestamp

```sql
v_position := v_position_start;
v_created_at := clock_timestamp(); -- After the lock, so that it is monotone with position.
```

`clock_timestamp()` returns the actual wall-clock time at the moment it is called, unlike `now()` /
`transaction_timestamp()`, which are frozen at the start of the transaction
([Current date/time](https://www.postgresql.org/docs/17/functions-datetime.html#FUNCTIONS-DATETIME-CURRENT)).

Taking it *after* the lock is acquired guarantees that if batch A got lower positions than batch B, A's `created_at`
is also earlier, because A held the lock first. With `now()` a batch that waited a long time for the lock could carry a
timestamp earlier than the batch it queued behind.

### Lines 127–131: idempotency probe

```sql
v_existing_commits := ARRAY(
    SELECT e.commit_id
    FROM __schema__.event_log AS e
    WHERE e.commit_id = ANY (p_commit_ids) AND e.commit_index = 0);
```

`ARRAY(subquery)` builds an array from a single-column result
([Array constructors](https://www.postgresql.org/docs/17/sql-expressions.html#SQL-SYNTAX-ARRAY-CONSTRUCTORS)).
`= ANY (array)` is true if the value equals any element
([`ANY`/`SOME` (array)](https://www.postgresql.org/docs/17/functions-comparisons.html#FUNCTIONS-COMPARISONS-ANY-SOME)),
and the planner can drive it from `event_log_commit_id_idx` as an index scan with an array of keys.

The index is partial on `commit_index = 0` ([Partial indexes](https://www.postgresql.org/docs/17/indexes-partial.html)),
so the query must repeat that predicate for the planner to consider the index. Because every request writes exactly
one row with `commit_index = 0`, the predicate also guarantees at most one row per commit id, which is why no
`DISTINCT` is needed even though a commit with N events occupies N rows.

The result is stable for the rest of the function because no other appender can insert while the lock is held. This
single statement replaces R separate lookups.

### Lines 133–140: stream head prefetch

```sql
SELECT coalesce(array_agg(coalesce(h.head, -1) ORDER BY s.stream_id), '{}'::bigint[]) INTO v_heads
FROM (SELECT DISTINCT d.stream_id FROM unnest(p_stream_ids) AS d(stream_id)) AS s
CROSS JOIN LATERAL (
    SELECT max(e.stream_position) AS head
    FROM __schema__.event_log AS e
    WHERE e.stream_id = s.stream_id) AS h;
```

Read it inside out:

1. `s`: the distinct stream ids in the batch. A batch may target the same stream several times; each stream is probed
   once.
2. `h`: for each such stream, the highest `stream_position` currently stored. `LATERAL` lets the subquery reference
   `s.stream_id` from the outer `FROM` item; without it the subquery could not see `s`
   ([`LATERAL` subqueries](https://www.postgresql.org/docs/17/queries-table-expressions.html#QUERIES-LATERAL)).
   Because it is an aggregate it always yields exactly one row, `NULL` if the stream has no events, so `CROSS JOIN`
   never drops a stream.
3. `coalesce(h.head, -1)`: encode "no events" as `-1`, so that "next stream position" is uniformly `head + 1`.
4. `array_agg(... ORDER BY s.stream_id)`: collapse the rows into one array sorted by stream id
   ([Aggregate expressions, `ORDER BY`](https://www.postgresql.org/docs/17/sql-expressions.html#SYNTAX-AGGREGATES)).
   The order is what makes the array addressable by `dense_rank()` in the request loop (line 147).
5. Outer `coalesce(..., '{}')`: `array_agg` over zero rows returns `NULL`, not an empty array. Unreachable here because
   R ≥ 1, but it keeps the variable non-null.

**Why `max()` in a lateral subquery rather than a `GROUP BY`.** The planner recognises `min`/`max` of an indexed column
with an equality condition on the leading index columns and rewrites it as "scan the `(stream_id, stream_position)`
index backwards from `stream_id`, take the first row". That is one index descent per stream regardless of how many
events the stream has. A `GROUP BY stream_id` over the same rows would have to read every event of every stream in the
batch. The general idea of using an index to satisfy ordering is in
[Indexes and `ORDER BY`](https://www.postgresql.org/docs/17/indexes-ordering.html); confirming the actual plan is a
job for [`EXPLAIN`](https://www.postgresql.org/docs/17/using-explain.html) on the statement with a representative
array.

### Lines 142–159: the request query

```sql
FOR v_req IN
    SELECT
        q.ord::integer AS idx,
        dense_rank() OVER (ORDER BY q.stream_id)::integer AS sid,
        row_number() OVER (PARTITION BY q.commit_id ORDER BY q.ord) AS commit_occurrence,
        q.kind,
        q.version,
        q.commit_id,
        q.event_count,
        1 + coalesce(
            sum(q.event_count) OVER (ORDER BY q.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),
            0) AS event_offset
    FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids, p_event_counts)
        WITH ORDINALITY AS q(stream_id, kind, version, commit_id, event_count, ord)
    ORDER BY q.ord
LOOP
```

`FOR record IN query LOOP` runs the query once and iterates its rows
([Looping through query results](https://www.postgresql.org/docs/17/plpgsql-control-structures.html#PLPGSQL-RECORDS-ITERATING)).
The query does all the set-oriented bookkeeping in one pass so the loop body only does per-row decisions.

- `WITH ORDINALITY` adds a `bigint` column `ord` numbering rows from 1 in array order
  ([Table functions](https://www.postgresql.org/docs/17/queries-table-expressions.html#QUERIES-TABLEFUNCTIONS)).
  `ord` is the request's 1-based index; `idx` is the same value cast to `integer`
  ([Type casts](https://www.postgresql.org/docs/17/sql-expressions.html#SQL-SYNTAX-TYPE-CASTS)).
- `sid = dense_rank() OVER (ORDER BY q.stream_id)`: the 1-based rank of the request's stream among the distinct
  stream ids in the batch, with ties (same stream) sharing a rank and no gaps. Because `v_heads` was built with
  `array_agg(... ORDER BY stream_id)` over the same distinct set, `v_heads[sid]` is that stream's head. Both orderings
  use the default collation of `text`, so they agree. See
  [Window functions](https://www.postgresql.org/docs/17/functions-window.html) and the
  [window function tutorial](https://www.postgresql.org/docs/17/tutorial-window.html).
- `commit_occurrence = row_number() OVER (PARTITION BY q.commit_id ORDER BY q.ord)`: 1 for the first request in
  the batch with a given commit id, 2 for the second, and so on. Only the first can be accepted.
- `event_offset = 1 + sum(event_count) over all earlier requests`: the 1-based index into the event arrays where this
  request's events start. The frame `ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING` sums strictly earlier rows;
  for the first row the frame is empty, `sum` returns `NULL`, and `coalesce` makes it 0 so the offset is 1.
  Frame syntax: [Window function calls](https://www.postgresql.org/docs/17/sql-expressions.html#SYNTAX-WINDOW-FUNCTIONS).
- `ORDER BY q.ord` guarantees the loop visits requests in batch order, which is what makes position assignment and the
  "first occurrence wins" rule deterministic.

The query contains two window functions with different orderings (`stream_id` and `ord`) plus a final `ORDER BY`, so
the executor sorts the R rows up to three times. R is small, so this is cheap, but it is a fixed cost worth knowing
about.

### Lines 160–166: per-request setup

```sql
request_index := v_req.idx - 1;
first_position := NULL;
last_position := NULL;

v_head := v_heads[v_req.sid];
observed_kind := CASE WHEN v_head < 0 THEN 0 ELSE 1 END;
observed_version := CASE WHEN v_head < 0 THEN NULL ELSE v_head END;
```

The output columns are reset for every request (they retain their previous values otherwise, since they are ordinary
variables). `request_index` is converted to the 0-based index the C# client uses.

`v_head` is the stream's *current* head as of this point in the batch, including any events accepted for the same
stream by earlier requests in the loop (see line 201). `observed_*` describe that head in the protocol's terms and are
emitted on every row; the client only reads them on conflict, but computing them unconditionally keeps the loop
branch-free. `CASE` expression: [Conditional expressions](https://www.postgresql.org/docs/17/functions-conditional.html#FUNCTIONS-CASE).

### Lines 168–173: duplicate detection

```sql
IF v_req.commit_occurrence > 1 OR v_req.commit_id = ANY (v_existing_commits) THEN
    status := 2;
    RETURN NEXT;
    CONTINUE;
END IF;
```

A request is a duplicate if its commit id already appeared earlier in this batch, or already exists in the table. Either
way it is reported as status 2 (Duplicate) and skipped; nothing is written for it and it consumes no positions.

`RETURN NEXT` emits the current values of the output columns as one result row and carries on. `CONTINUE` jumps to the
next loop iteration ([`CONTINUE`](https://www.postgresql.org/docs/17/plpgsql-control-structures.html#PLPGSQL-CONTROL-STRUCTURES-LOOPS-CONTINUE)).

`= ANY (v_existing_commits)` is a linear scan of the array; it is evaluated once per request, so its cost is
R × |existing|. In the common case the existing-commit array is empty.

Note that a duplicate is reported with status 2 whatever its expectation was; the client treats Duplicate as success,
which is the correct outcome for a retried batch.

### Lines 175–186: expectation check

```sql
v_matches := CASE v_req.kind
    WHEN 0 THEN true              -- Any
    WHEN 1 THEN v_head < 0        -- DoesNotExist
    WHEN 2 THEN v_head >= 0       -- Exists
    WHEN 3 THEN v_head = v_req.version   -- AtVersion
END;

IF NOT v_matches THEN
    status := 1;
    RETURN NEXT;
    CONTINUE;
END IF;
```

Optimistic concurrency. `v_head` is `-1` for an absent stream, so:

- `DoesNotExist` matches only when no events exist;
- `Exists` matches when at least one does;
- `AtVersion n` matches when the last stored `stream_position` is exactly `n`. Because `version >= 0` was validated
  and an absent stream is `-1`, `AtVersion` can never match an absent stream.

A failed expectation is status 1 (Conflict), reported with the `observed_*` columns already set on lines 165–166 so
the client can build a `StreamAppendConflictException` with what the store actually saw.

Because the head is kept current within the batch (line 201), two requests for the same stream that both expect
version 5 cannot both succeed: the second observes 5 + (events of the first) and conflicts, exactly as it would if the
two had arrived in separate batches.

### Lines 188–201: accepting a request

```sql
v_accepted := v_accepted + 1;
v_acc_index[v_accepted] := v_req.idx;
v_acc_first_pos[v_accepted] := v_position;
v_acc_first_ver[v_accepted] := v_head + 1;
v_acc_event_offset[v_accepted] := v_req.event_offset;
v_acc_event_count[v_accepted] := v_req.event_count;

status := 0;
first_position := v_position;
last_position := v_position + v_req.event_count - 1;
RETURN NEXT;

v_position := v_position + v_req.event_count;
v_heads[v_req.sid] := v_head + v_req.event_count;
```

Five things happen:

1. The request is appended to the accepted list. Assigning to an array subscript one past the end extends the array
   ([Modifying arrays](https://www.postgresql.org/docs/17/arrays.html#ARRAYS-MODIFYING)). PL/pgSQL keeps local
   arrays in an "expanded" in-memory form so that element assignment does not copy the whole array on each write.
2. The request's global positions are decided: `first_position` is the cursor, `last_position` is cursor + count − 1.
3. The request's first stream position is `head + 1`, so for a new stream (head −1) events start at 0.
4. Status 0 (Appended) is emitted.
5. The cursor advances by the event count, and the in-memory head for that stream advances too, so the next request
   to the same stream sees the events this one will insert.

Nothing has been written yet. All of this is plain variable manipulation.

### Lines 204–206: nothing accepted

```sql
IF v_accepted = 0 THEN
    RETURN;
END IF;
```

If every request was a conflict or duplicate there is nothing to insert and, importantly, the sequence row is **not**
updated. That avoids creating a dead tuple on the hot row for nothing. The result rows accumulated so far are returned.

### Lines 208–230: the single `INSERT`

```sql
INSERT INTO __schema__.event_log (
    position, stream_id, stream_position, commit_id, commit_index,
    event_name, event_data, event_data_bytes, metadata, created_at)
SELECT
    a.first_pos + g.k,
    req.stream_id,
    a.first_ver + g.k,
    req.commit_id,
    g.k,
    ev.event_name,
    ev.event_data,
    ev.event_data_bytes,
    ev.metadata,
    v_created_at
FROM unnest(v_acc_index, v_acc_first_pos, v_acc_first_ver, v_acc_event_offset, v_acc_event_count)
    AS a(idx, first_pos, first_ver, event_offset, event_count)
JOIN unnest(p_stream_ids, p_commit_ids) WITH ORDINALITY AS req(stream_id, commit_id, ord) ON req.ord = a.idx
CROSS JOIN LATERAL generate_series(0, a.event_count - 1) AS g(k)
JOIN unnest(p_event_names, p_event_data, p_event_data_bytes, p_metadata)
    WITH ORDINALITY AS ev(event_name, event_data, event_data_bytes, metadata, ord)
    ON ev.ord = a.event_offset + g.k;
```

`INSERT ... SELECT` writes every row the query produces
([INSERT](https://www.postgresql.org/docs/17/sql-insert.html)). The query reconstructs the event rows from the
accepted list plus the original parameter arrays:

| Alias | Rows | Source | Role |
|---|---|---|---|
| `a` | one per accepted request | the `v_acc_*` arrays | the decisions from the loop: where the request's events go |
| `req` | one per request in the batch | `p_stream_ids`, `p_commit_ids` with ordinality | looked up by `a.idx` to recover the stream id and commit id (they were not copied into `v_acc_*`) |
| `g` | `event_count` per accepted request | `generate_series(0, n-1)` ([Set-returning functions](https://www.postgresql.org/docs/17/functions-srf.html)) | `k` is the event's index within its request, and becomes `commit_index` |
| `ev` | one per event in the batch | the four event arrays with ordinality | looked up by `event_offset + k` to fetch the payload |

Row by row the arithmetic is:

- `position = first_pos + k`, contiguous within the request and across accepted requests, because the loop advanced the
  cursor by exactly `event_count`;
- `stream_position = first_ver + k`, contiguous within the stream;
- `commit_index = k`;
- `created_at = v_created_at`, identical for the whole batch.

The comment says the arrays are "read through unnest rather than subscripted". This is about cost: `unnest` walks an
array once, whereas a correlated `p_event_names[a.event_offset + g.k]` style lookup would be a separate subscript
evaluation per row. In practice the planner joins `g × ev` and `a × req` with hash or merge joins over these small
row sets, so the whole statement is roughly linear in E.

The two unique constraints on `event_log` are checked as each row is inserted. Given the lock and the head prefetch,
they should never fail; if they ever do, it indicates the invariants were violated (for example, a write path that
bypassed this function), and the whole batch rolls back.

### Line 233: advance the sequence

```sql
UPDATE __schema__.sequences AS s SET next = v_position WHERE s.name = c_sequence_name;
```

`v_position` is `v_position_start` plus the number of events actually accepted, so the next batch starts exactly
after the last row written. Conflicts and duplicates consumed no positions, hence no gaps
([UPDATE](https://www.postgresql.org/docs/17/sql-update.html)).

This is the same row that was locked at line 114, so the `UPDATE` does not block. With `fillfactor = 50` and no
indexed column changing, it should be a HOT update, keeping the primary-key index untouched. A HOT chain still leaves
one dead tuple per batch on the page until it is pruned, which is why frequent batches keep the page "warm" but never
let it grow.

### Lines 235–236: end

```sql
RETURN;
END
```

Ends the function and hands the accumulated result rows to the caller. The transaction then commits and the row lock
is released; only now can the next waiting batch's `SELECT ... FOR UPDATE` proceed and re-read `next`.

## 6. Worked example

Batch of four requests, sequence row `next = 100`, stream `A` has events 0..2 (head 2), stream `B` does not exist.

| idx | stream | kind | version | commit | events |
|---|---|---|---|---|---|
| 1 | A | 3 AtVersion | 2 | c1 | 2 |
| 2 | B | 1 DoesNotExist | | c2 | 1 |
| 3 | A | 3 AtVersion | 2 | c3 | 1 |
| 4 | B | 0 Any | | c2 | 3 |

Prefetch: distinct streams sorted → `[A, B]`; `v_heads = [2, -1]`. `v_existing_commits = []`.

Request query adds `sid`, `commit_occurrence`, `event_offset`:

| idx | sid | commit_occurrence | event_offset |
|---|---|---|---|
| 1 | 1 | 1 | 1 |
| 2 | 2 | 1 | 3 |
| 3 | 1 | 1 | 4 |
| 4 | 2 | 2 | 5 |

Loop, with `v_position` starting at 100:

| idx | v_head | matches? | result | positions | after |
|---|---|---|---|---|---|
| 1 | 2 | 2 = 2 ✔ | Appended | 100–101, stream 3–4 | `v_position = 102`, `v_heads = [4, -1]` |
| 2 | −1 | absent ✔ | Appended | 102, stream 0 | `v_position = 103`, `v_heads = [4, 0]` |
| 3 | 4 | 4 = 2 ✘ | Conflict, observed AtVersion 4 | | unchanged |
| 4 | 0 | commit_occurrence 2 | Duplicate | | unchanged |

Insert produces three rows (positions 100, 101, 102), and the sequence row becomes `next = 103`. Request 3 sees the
head as if request 1 had already committed, which it will have by the time anyone can observe it.

## 7. Correctness properties and why they hold

**Global positions are gap-free and in commit order.** The only writer of `sequences.next` is line 233, executed under
the row lock taken at line 114 and held to commit. A batch that raises never reaches line 233 and rolls back its
inserts, so the counter only moves when rows are committed. Waiting batches re-read the committed value under READ
COMMITTED (guarded at line 65).

**Stream positions are contiguous per stream.** The head is read under the lock (line 135), and within a batch it is
advanced in memory (line 201) as requests are accepted, so consecutive requests to one stream chain correctly. No other
writer can change a head while the lock is held.

**Expectations are checked against the true head.** Same reasoning; `v_head` is the exact value that will exist when
this batch commits.

**Retrying a batch is safe.** If the client's statement fails after the server committed (for example, the connection
drops while the result set is in flight), the retry finds every commit id in `event_log` (line 128) and reports each
request as Duplicate. Requests that conflicted the first time still conflict, because the head has not moved for them.

**One result row per request.** Every branch of the loop body (duplicate, conflict, accepted) executes exactly one
`RETURN NEXT`, and the loop visits every request. `AppendBatchCommand` still checks for missing rows defensively.

**Partial batches are never visible.** All writes happen in one transaction; any `RAISE` or constraint failure rolls
everything back, and the accepted/conflict/duplicate results of that batch are discarded with it.

## 8. Cost model: where the time goes

For a batch of R requests, S distinct streams, and E events, of which E′ are accepted:

| Step | Lines | Table access | Cost driver |
|---|---|---|---|
| Argument validation | 65–111 | none | O(R + E) in SQL over the arrays; three `EXISTS` scans and one `sum`. |
| Lock | 114 | 1 row of `sequences` | Wait time behind the previous batch. This is the serialisation point. |
| Commit-id probe | 128 | index scan on `commit_id` with R keys | O(R log N) index probes, normally all misses. |
| Head prefetch | 135 | S backwards index probes on `(stream_id, stream_position)` | O(S log N), independent of stream length. |
| Request query | 144 | none | Up to three sorts of R rows for the window functions and `ORDER BY`. |
| Loop body | 160–202 | none | O(R) interpreted PL/pgSQL, plus O(R × |existing|) for the `= ANY` check. |
| Insert | 210 | E′ heap inserts, 2 index inserts each | O(E′ log N) plus WAL volume proportional to payload size. |
| Sequence update | 233 | 1 HOT update | Constant. |
| Commit | | | WAL flush (`fsync`), typically the single largest fixed cost per batch. |

Things that are fixed per batch and therefore amortised by batching: lock acquisition, plan lookup, the request query's
sorts, the sequence update and the commit's WAL flush. Things that scale with the batch: validation, the insert and the
per-stream/per-commit index probes.

When measuring, the natural questions are:

- How much of a batch's wall time is the commit flush versus the statements? (`synchronous_commit`, WAL settings and
  disk latency dominate this; see [Asynchronous Commit](https://www.postgresql.org/docs/17/wal-async-commit.html).)
- Does the head prefetch actually get the backwards index-scan-with-limit plan? Check with
  [`EXPLAIN (ANALYZE, BUFFERS)`](https://www.postgresql.org/docs/17/using-explain.html) on the statement in isolation,
  or with the [`auto_explain`](https://www.postgresql.org/docs/17/auto-explain.html) module to capture plans from
  inside the function.
- How does the per-row interpreter cost of the loop compare with the insert as R grows? PL/pgSQL profiling is easiest
  with [`pg_stat_statements`](https://www.postgresql.org/docs/17/pgstatstatements.html) with
  `pg_stat_statements.track = all`, which reports the statements nested inside the function separately.
- How long do waiters spend blocked on the sequence row under concurrent load?
  [`pg_stat_activity`](https://www.postgresql.org/docs/17/monitoring-stats.html#MONITORING-PG-STAT-ACTIVITY-VIEW)
  shows `wait_event_type = Lock` for them, and
  [`pg_locks`](https://www.postgresql.org/docs/17/view-pg-locks.html) shows the tuple lock.
