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
    stream_id        text        COLLATE "C" NOT NULL,   -- byte-wise comparison: see below
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
CREATE UNIQUE INDEX event_log_commit_id_idx ON __schema__.event_log (commit_id) WHERE commit_index = 0;

CREATE TABLE __schema__.sequences (name text PRIMARY KEY, next bigint NOT NULL) WITH (fillfactor = 50);
INSERT INTO __schema__.sequences VALUES ('event_log', 0) ON CONFLICT DO NOTHING;
```

Three indexes matter to the function:

| Index | Used by |
|---|---|
| `event_log_pkey (position)` | The final `INSERT` (uniqueness check on every new row). |
| `event_log_stream_id_stream_position_key (stream_id, stream_position)` | The head prefetch (one backwards probe per distinct stream) and the `INSERT` uniqueness check. |
| `event_log_commit_id_idx (commit_id) WHERE commit_index = 0` | The idempotency probe. Partial: one entry per request rather than per event, since every request writes exactly one row with `commit_index = 0`. Unique, so a commit id can only ever be appended once even by a writer that bypasses the function. |

`stream_id` carries the `"C"` collation ([Collation support](https://www.postgresql.org/docs/17/collation.html)).
The column is only ever compared for equality, and under a locale collation every btree comparison on the stream
index runs the collator over the ids' common prefix. With ids shaped like `<category>-<guid>` that doubled the cost
of a batch; byte-wise comparison removes it. Inside the function, stream ids from the parameter array are only ever
partitioned, grouped and joined against each other (under the parameter's collation), and compared with the column
in the head probe (under the column's collation, which is what makes the index usable), so the change is invisible to
the function's logic.

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

The batch is committed with a fixed number of SQL statements regardless of how many requests or events it contains:

```
 1. validate arguments             two statements over the arrays (raise => whole batch fails)
 2. SELECT ... FOR UPDATE          lock the sequence row  ──────────┐  serialises all appenders,
 3. probe existing commit ids      1 index scan                     │  held until COMMIT
 4. one statement:                                                  │
      prefetch stream heads        1 index probe per distinct stream│
      evaluate requests            recursive CTE, 1 iteration per   │
                                   repeat of a stream               │
      INSERT accepted events       E' rows                          │
      UPDATE sequence row          1 row                            │
      return result rows           ──────────────────────────── COMMIT
```

Steps 3 and 4 read the *committed* state that exists at the time the lock is acquired. Because every other appender is
blocked on the same lock, that state cannot change until this batch commits, so the evaluation in step 4 can reason
about it without re-checking.

Step 4 is one statement on purpose. Every per-request decision is a column computed by SQL, so PL/pgSQL pays its
interpreter cost once per batch rather than once per request. The only sequential dependency in the problem, that a
request to a stream must see the rows an earlier request to the same stream will insert, is expressed as a recursive
CTE that advances every stream by one request per iteration.

## 5. Line-by-line walkthrough

### Lines 1–20: header comment

The comment is the design summary. Three claims in it are worth pinning to the code:

- "All appenders serialize on the event_log sequence row, whose lock is held until commit" → lines 107–110 and the
  autocommit call site.
- "Each request is evaluated independently ... Only protocol violations raise" → the validation block (lines 60–99)
  raises; the evaluating statement (lines 128–238) never does.
- "fixed number of statements regardless of its size" → the table statements at lines 107, 122 and 128.

### Lines 21–30: signature

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
them in binary form, so there is no text parsing on the server for anything except the `jsonb` elements, which are
parsed at bind time.

### Lines 31–37: `RETURNS TABLE`

```sql
RETURNS TABLE (
    request_index    integer,
    status           smallint,
    observed_kind    smallint,
    observed_version bigint,
    first_position   bigint,
    last_position    bigint)
```

This declares a set-returning function. In PL/pgSQL the six output columns double as **local variables**, which is why
the query at line 128 avoids using those names for anything it computes: an unqualified `status` inside it would be
substituted with the variable. See
[Returning from a function](https://www.postgresql.org/docs/17/plpgsql-control-structures.html#PLPGSQL-STATEMENTS-RETURNING).

Important implementation detail from that page: PL/pgSQL does **not** stream rows to the caller. `RETURN QUERY`
runs the query to completion into a tuplestore, and the whole set is handed back when the function finishes. So the
client sees no rows until after the insert, the update and the commit, which is exactly what the completion semantics
need.

### Line 38: `LANGUAGE plpgsql`

The body is procedural. PL/pgSQL is interpreted; each embedded SQL statement is prepared once per session and cached,
and expressions such as `coalesce(cardinality(p_stream_ids), 0)` are themselves tiny `SELECT`s executed through the
SQL engine (with a fast path for simple expressions). See
[PL/pgSQL under the hood](https://www.postgresql.org/docs/17/plpgsql-implementation.html). Since the function no
longer loops over requests, the interpreter runs a fixed handful of expressions per batch.

### Line 39: `SET plan_cache_mode = force_generic_plan`

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

### Line 40 and 241: `AS $fn$ ... $fn$`

Dollar quoting delimits the function body so that ordinary single quotes inside it need no escaping.
See [Dollar-quoted string constants](https://www.postgresql.org/docs/17/sql-syntax-lexical.html#SQL-SYNTAX-DOLLAR-QUOTING).

### Lines 41–50: `DECLARE`

See [Declarations](https://www.postgresql.org/docs/17/plpgsql-declarations.html).

| Variable | Purpose |
|---|---|
| `c_sequence_name CONSTANT text := 'event_log'` | Key of the row in `sequences`. `CONSTANT` makes assignment a compile error. |
| `v_request_count integer` | R. |
| `v_event_total bigint` | E, the sum of `p_event_counts`. `bigint` because `sum(integer)` returns `bigint`. |
| `v_invalid_count boolean` | Whether any event count is null or non-positive. |
| `v_invalid_request boolean` | Whether any request fails the semantic checks. |
| `v_position_start bigint` | The value read from the sequence row: the global position the first accepted event will get. |
| `v_created_at timestamptz` | One timestamp stamped on every event in the batch. |
| `v_existing_commits uuid[]` | Commit ids from the batch that are already in `event_log`. |

Everything per-request lives inside the query at line 128; nothing per-request is held in a PL/pgSQL variable.

### Lines 52–58: isolation-level guard

```sql
IF current_setting('transaction_isolation') <> 'read committed' THEN
    RAISE EXCEPTION 'append_events requires READ COMMITTED isolation (current: %)', ...
        USING ERRCODE = 'invalid_transaction_state';
END IF;
```

`current_setting` reads a run-time parameter as text ([Configuration settings functions](https://www.postgresql.org/docs/17/functions-admin.html#FUNCTIONS-ADMIN-SET)).
`transaction_isolation` reports the isolation level of the current transaction
([`transaction_isolation`](https://www.postgresql.org/docs/17/runtime-config-client.html#GUC-TRANSACTION-ISOLATION)).

Why the guard exists: the `SELECT ... FOR UPDATE` at line 107 may block behind another batch. When that batch commits,
what happens next depends on the isolation level:

- Under **READ COMMITTED**, the blocked statement wakes up, re-reads the *newly committed* version of the row and
  locks that. This is the behaviour the design relies on: the next batch sees the updated `next` value. Just as
  important, every later statement in the function takes a fresh snapshot, so the heads read at line 153 include
  the rows the previous batch committed.
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

### Lines 60–68: request count and request arrays must agree

```sql
v_request_count := coalesce(cardinality(p_stream_ids), 0);

IF coalesce(cardinality(p_expected_kinds), 0) <> v_request_count OR
   ... THEN
    RAISE EXCEPTION 'request arrays must all have length %', v_request_count
        USING ERRCODE = 'invalid_parameter_value';
END IF;
```

`cardinality` returns the total element count of an array, and returns `NULL` for a `NULL` array; `coalesce` turns
that into 0 so a `NULL` argument is treated as an empty array. See
[`cardinality`](https://www.postgresql.org/docs/17/functions-array.html) and
[`COALESCE`](https://www.postgresql.org/docs/17/functions-conditional.html#FUNCTIONS-COALESCE-NVL-IFNULL).

Structure-of-arrays encoding is only meaningful if the parallel arrays line up. Note `cardinality` counts `NULL`
elements, so `p_expected_versions` (which legitimately contains `NULL`s for non-`AtVersion` requests) still has length R.
This check has to come first: the multi-array `unnest` on the next statement pads shorter arrays with `NULL`s
rather than failing, so it would mask a length mismatch.

### Lines 70–81: one validation pass over the requests

```sql
SELECT
    coalesce(sum(r.event_count), 0),
    bool_or(r.event_count IS NULL OR r.event_count <= 0),
    bool_or(r.stream_id IS NULL OR r.stream_id = ''
        OR r.commit_id IS NULL
        OR r.kind IS NULL OR r.kind NOT BETWEEN 0 AND 3
        OR (r.kind = 3 AND (r.version IS NULL OR r.version < 0))
        OR (r.kind <> 3 AND r.version IS NOT NULL))
INTO v_event_total, v_invalid_count, v_invalid_request
FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids, p_event_counts)
    AS r(stream_id, kind, version, commit_id, event_count);
```

Multi-argument `unnest` zips several arrays into one row set, one row per request; this form is only allowed in the
`FROM` clause. See [Table functions](https://www.postgresql.org/docs/17/queries-table-expressions.html#QUERIES-TABLEFUNCTIONS).
Three things are computed in the single pass, using aggregates
([Aggregate functions](https://www.postgresql.org/docs/17/functions-aggregate.html)):

- `sum(event_count)` is E. `sum` ignores `NULL`s, which is why the null check is separate, and returns `NULL` over
  no rows, which the `coalesce` turns into 0 so that an empty batch compares equal to empty event arrays below.
- `bool_or(...)` is true if any event count is missing or non-positive. A zero count would make a request occupy no
  events yet still "append", which would break the position arithmetic (`last_position = first_position + count - 1`
  would be less than `first_position`).
- `bool_or(...)` over the semantic rules:

| Rule | Reason |
|---|---|
| `stream_id` not null or empty | Mirrors the `CHECK (stream_id <> '')` on `event_log`; failing here is a clean protocol error rather than a constraint violation mid-insert. |
| `commit_id` not null | It is the idempotency key and an `= ANY` test against `NULL` would never match. |
| `kind` in 0..3 | Any other value would make the `CASE` at line 177 yield `NULL`, which the outer `CASE` would treat as false and silently report as a conflict. Validating up front keeps that path unreachable. |
| kind 3 ⇒ version present and ≥ 0 | `AtVersion` needs a version; stream positions are 0-based. |
| kind ≠ 3 ⇒ version null | Catches a client that sends a version with the wrong kind. |

`SELECT ... INTO` several PL/pgSQL variables at once is described in
[Executing a query with a single-row result](https://www.postgresql.org/docs/17/plpgsql-statements.html#PLPGSQL-STATEMENTS-SQL-ONEROW).
Previously these were three separate statements; folding them saves two executor start-ups per batch.

### Lines 83–99: raising in a fixed order

```sql
IF v_invalid_count THEN
    RAISE EXCEPTION 'event counts must be positive' ...
END IF;

IF coalesce(cardinality(p_event_names), 0) <> v_event_total OR ... THEN
    RAISE EXCEPTION 'event arrays must all have length %', v_event_total ...
END IF;

IF v_invalid_request THEN
    RAISE EXCEPTION 'invalid request: check stream ids, commit ids, expected kinds and versions' ...
END IF;
```

The checks are raised in the same order the earlier per-statement version used, so a client bug reports the same
first error it always did. The event-array length check sits in the middle because E is only meaningful once the
counts are known to be valid. If the client's flattening were ever wrong, the `JOIN ... ON ev.ord = d.event_offset +
g.k` in the insert would silently drop rows or attach the wrong payloads, so this is caught up front.

### Lines 101–104: the empty batch

```sql
IF v_request_count = 0 THEN
    RETURN;
END IF;
```

A bare `RETURN` in a set-returning function ends execution and returns whatever has been accumulated: here, nothing.
The lock is never taken for an empty batch. (The client never sends one, but the function is defensive.) The return
sits after the validation rather than before it so that an empty batch is held to the same contract as any other:
zero requests with non-empty event arrays is a protocol violation and raises, rather than being silently accepted.

### Lines 106–115: the lock

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
the `next` value the previous batch wrote at line 225. Hence:

- global positions are handed out in commit order;
- there are no gaps, because a rolled-back batch never updated the row;
- throughput of the whole store is bounded by the critical section between this line and commit, which is why the
  client batches.

**This must stay a separate statement from the one at line 128.** Under READ COMMITTED each statement takes its own
snapshot. If the head prefetch were part of the same statement as the `FOR UPDATE`, it would read from a snapshot
taken before the lock was acquired, and after waiting behind another batch it would see stale heads. Keeping the lock
in its own statement is what guarantees the evaluating statement sees the previous batch's rows.

`FOUND` is set by `SELECT INTO` and is false when no row matched
([Obtaining the result status](https://www.postgresql.org/docs/17/plpgsql-statements.html#PLPGSQL-STATEMENTS-DIAGNOSTICS)).
A missing row means `schema.sql` was never run.

### Line 117: timestamp

```sql
v_created_at := clock_timestamp(); -- After the lock, so that it is monotone with position.
```

`clock_timestamp()` returns the actual wall-clock time at the moment it is called, unlike `now()` /
`transaction_timestamp()`, which are frozen at the start of the transaction
([Current date/time](https://www.postgresql.org/docs/17/functions-datetime.html#FUNCTIONS-DATETIME-CURRENT)).

Taking it *after* the lock is acquired guarantees that if batch A got lower positions than batch B, A's `created_at`
is also earlier, because A held the lock first. With `now()` a batch that waited a long time for the lock could carry a
timestamp earlier than the batch it queued behind.

### Lines 119–126: idempotency probe

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
`DISTINCT` is needed even though a commit with N events occupies N rows. The index is also unique, so that guarantee
does not depend on this function being the only writer: a direct insert that repeats a committed id fails on the
index rather than turning a later probe into a false negative.

The result is stable for the rest of the function because no other appender can insert while the lock is held. This
single statement replaces R separate lookups. It is kept separate from the statement below because an array-keyed
index scan is cheaper than a correlated `EXISTS` per request row would be.

### Lines 128–238: the evaluating statement

```sql
RETURN QUERY
WITH RECURSIVE request AS (...),
head AS (...),
chain AS (...),
decided AS (...),
inserted AS (INSERT ... RETURNING 1),
advanced AS (UPDATE ...)
SELECT ... FROM decided AS d ORDER BY d.ord;
```

Everything from "which requests are accepted" to "return the result rows" is one statement built from common table
expressions ([`WITH` queries](https://www.postgresql.org/docs/17/queries-with.html)). Two features of `WITH` carry the
design:

- **Data-modifying CTEs** (`inserted`, `advanced`) run exactly once regardless of how the final `SELECT` uses them,
  and all sub-statements see the same snapshot
  ([Data-modifying statements in `WITH`](https://www.postgresql.org/docs/17/queries-with.html#QUERIES-WITH-MODIFYING)).
  That is what lets one statement insert, update and return.
- **`WITH RECURSIVE`** (`chain`) is how a request learns what earlier requests to its stream decided
  ([Recursive queries](https://www.postgresql.org/docs/17/queries-with.html#QUERIES-WITH-RECURSIVE)).

`RETURN QUERY` appends the statement's result rows to the function's output
([`RETURN QUERY`](https://www.postgresql.org/docs/17/plpgsql-control-structures.html#PLPGSQL-STATEMENTS-RETURNING-RETURN-QUERY)).
The CTEs are walked in order below.

### Lines 129–152: `request`

```sql
request AS (
    SELECT
        r.ord, r.stream_id, r.kind, r.version, r.commit_id, r.event_count,
        row_number() OVER (PARTITION BY r.stream_id ORDER BY r.ord) AS nth,
        (row_number() OVER (PARTITION BY r.commit_id ORDER BY r.ord) > 1
            OR r.commit_id = ANY (v_existing_commits)) AS duplicate,
        1 + coalesce(
            sum(r.event_count) OVER (ORDER BY r.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),
            0) AS event_offset
    FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids, p_event_counts)
        WITH ORDINALITY AS r(stream_id, kind, version, commit_id, event_count, ord)
)
```

One row per request with the bookkeeping that does not depend on the database:

- `WITH ORDINALITY` adds a `bigint` column `ord` numbering rows from 1 in array order
  ([Table functions](https://www.postgresql.org/docs/17/queries-table-expressions.html#QUERIES-TABLEFUNCTIONS)).
  `ord` is the request's 1-based index and the batch order that every later step respects.
- `nth = row_number() OVER (PARTITION BY stream_id ORDER BY ord)`: the request's position among the requests to
  its own stream, 1 for the first. This is the recursion key. See
  [Window functions](https://www.postgresql.org/docs/17/functions-window.html) and the
  [window function tutorial](https://www.postgresql.org/docs/17/tutorial-window.html).
- `duplicate`: true if an earlier request in the batch carries the same commit id, or the id already exists in the
  log. "Earlier" is by `ord`, so the first occurrence in batch order wins, even across different streams.
- `event_offset = 1 + sum(event_count) over all earlier requests`: the 1-based index into the event arrays where this
  request's events start. The frame `ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING` sums strictly earlier rows;
  for the first row the frame is empty, `sum` returns `NULL`, and `coalesce` makes it 0 so the offset is 1.
  Frame syntax: [Window function calls](https://www.postgresql.org/docs/17/sql-expressions.html#SYNTAX-WINDOW-FUNCTIONS).

Three window functions with three different orderings (`stream_id`, `commit_id`, `ord`) mean the executor sorts the
R rows up to three times. R is small, so this is a fixed cost of well under a millisecond. `request` is referenced by
three later CTEs, so the planner materialises it once
([CTE materialization](https://www.postgresql.org/docs/17/queries-with.html#QUERIES-WITH-CTE-MATERIALIZATION)).

### Lines 153–163: `head`

```sql
head AS (
    SELECT s.stream_id, coalesce(h.head, -1) AS head
    FROM (SELECT DISTINCT r.stream_id FROM request AS r) AS s
    CROSS JOIN LATERAL (
        SELECT max(e.stream_position) AS head
        FROM __schema__.event_log AS e
        WHERE e.stream_id = s.stream_id) AS h
)
```

One row per distinct stream in the batch with its current head, or `-1` if the stream has no events, so that "next
stream position" is uniformly `head + 1`.

`LATERAL` lets the subquery reference `s.stream_id` from the outer `FROM` item
([`LATERAL` subqueries](https://www.postgresql.org/docs/17/queries-table-expressions.html#QUERIES-LATERAL)). Because
it is an aggregate it always yields exactly one row, `NULL` if the stream has no events, so `CROSS JOIN` never drops
a stream.

**Why `max()` in a lateral subquery rather than a `GROUP BY`.** The planner recognises `min`/`max` of an indexed column
with an equality condition on the leading index columns and rewrites it as "scan the `(stream_id, stream_position)`
index backwards from `stream_id`, take the first row". That is one index descent per stream regardless of how many
events the stream has. A `GROUP BY stream_id` over the same rows would have to read every event of every stream in the
batch. The general idea of using an index to satisfy ordering is in
[Indexes and `ORDER BY`](https://www.postgresql.org/docs/17/indexes-ordering.html); confirming the actual plan is a
job for [`EXPLAIN`](https://www.postgresql.org/docs/17/using-explain.html) on the statement with a representative
array.

This is where the stream id collation matters (section 2): each index descent compares the parameter's stream id
against index keys, and with a locale collation over long common prefixes those comparisons dominated the batch.

### Lines 164–186: `chain`

```sql
chain AS (
    SELECT h.stream_id, 0::bigint AS nth, h.head,
           NULL::bigint AS ord, NULL::smallint AS req_status, NULL::bigint AS observed
    FROM head AS h
    UNION ALL
    SELECT r.stream_id, r.nth,
           CASE WHEN d.req_status = 0 THEN c.head + r.event_count ELSE c.head END,
           r.ord, d.req_status, c.head
    FROM chain AS c
    JOIN request AS r ON r.stream_id = c.stream_id AND r.nth = c.nth + 1
    CROSS JOIN LATERAL (
        SELECT (CASE
                    WHEN r.duplicate THEN 2
                    WHEN CASE r.kind
                             WHEN 0 THEN true
                             WHEN 1 THEN c.head < 0
                             WHEN 2 THEN c.head >= 0
                             WHEN 3 THEN c.head = r.version
                         END THEN 0
                    ELSE 1
                END)::smallint AS req_status) AS d
)
```

This is the request evaluation. A recursive CTE has a non-recursive term (before `UNION ALL`) that seeds a working
table, and a recursive term that is re-run against the previous iteration's rows until it produces none
([Recursive queries](https://www.postgresql.org/docs/17/queries-with.html#QUERIES-WITH-RECURSIVE)).

- **Seed:** one row per stream at `nth = 0` carrying the stored head. `ord`, `req_status` and `observed` are `NULL`
  because no request has been decided yet; the casts fix the column types for the whole CTE.
- **Iteration n:** join the rows at `nth = n - 1` to the request with `nth = n` on the same stream. Every stream that
  has an n-th request produces exactly one row; streams with fewer requests drop out. The lateral subquery `d`
  computes the decision once so it can be used in two output columns:
  - `duplicate` wins over everything, giving status 2 without touching the head.
  - Otherwise the expectation is checked against the running head `c.head`: `Any` always matches, `DoesNotExist`
    needs `-1`, `Exists` needs `≥ 0`, `AtVersion v` needs exactly `v`. Because versions are validated `≥ 0`,
    `AtVersion` can never match an absent stream.
  - Accepted (status 0) advances the head by the event count; conflict (status 1) leaves it.
- The row also records `observed = c.head`, the head this request saw, which becomes `observed_version` in the
  result and the base of its stream positions in the insert.

The number of iterations is the largest number of requests any one stream has in the batch. Under concurrent load
from many aggregates that is 1, and the whole evaluation is a single join. A batch of 500 requests to one stream
runs 500 iterations, each a join of a one-row working table against `request`; measured, that costs about the same
as the interpreted loop it replaced.

Evaluation order across streams differs from batch order: iteration 1 handles the first request of every stream,
whatever its `ord`. That is safe because the only cross-request dependencies are the per-stream head (respected by
`nth`) and first-occurrence-wins for commit ids (already decided by `ord` in `request`). Positions are assigned
afterwards in `ord` order, so nothing observable depends on evaluation order.

Restrictions worth knowing when editing: the recursive term may reference `chain` only once, and may not contain
aggregates or window functions over it. The lateral subquery and the `CASE` expressions are fine.

### Lines 187–199: `decided`

```sql
decided AS (
    SELECT
        r.ord, r.stream_id, r.commit_id, r.event_count, r.event_offset,
        c.req_status, c.observed,
        v_position_start + coalesce(
            sum(r.event_count) FILTER (WHERE c.req_status = 0)
                OVER (ORDER BY r.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),
            0) AS first_pos
    FROM chain AS c
    JOIN request AS r ON r.ord = c.ord
)
```

Joins the decisions back to the request rows (the seed rows have `NULL` `ord` and fall out of the join) and assigns
global positions. `first_pos` is the sequence value plus the event count of every *accepted* request earlier in batch
order, so accepted requests get contiguous positions in `ord` order and conflicts and duplicates consume none.
`FILTER` restricts which rows an aggregate sees
([Aggregate expressions](https://www.postgresql.org/docs/17/sql-expressions.html#SYNTAX-AGGREGATES)); combined with
the same window frame as `event_offset` it is a running sum over accepted rows only.

### Lines 200–224: `inserted`

```sql
inserted AS (
    INSERT INTO __schema__.event_log (
        position, stream_id, stream_position, commit_id, commit_index,
        event_name, event_data, event_data_bytes, metadata, created_at)
    SELECT
        d.first_pos + g.k,
        d.stream_id,
        d.observed + 1 + g.k,
        d.commit_id,
        g.k,
        ev.event_name, ev.event_data, ev.event_data_bytes, ev.metadata,
        v_created_at
    FROM decided AS d
    CROSS JOIN LATERAL generate_series(0, d.event_count - 1) AS g(k)
    JOIN unnest(p_event_names, p_event_data, p_event_data_bytes, p_metadata)
        WITH ORDINALITY AS ev(event_name, event_data, event_data_bytes, metadata, ord)
        ON ev.ord = d.event_offset + g.k
    WHERE d.req_status = 0
    RETURNING 1
)
```

`INSERT ... SELECT` writes every row the query produces ([INSERT](https://www.postgresql.org/docs/17/sql-insert.html)).
Per accepted request, `generate_series` ([Set-returning functions](https://www.postgresql.org/docs/17/functions-srf.html))
produces one row per event with `k` as the event's index within its request, and the event arrays are joined by
ordinal to fetch the payload. Row by row:

- `position = first_pos + k`, contiguous within the request and across accepted requests;
- `stream_position = observed + 1 + k`, contiguous within the stream because `observed` is the running head;
- `commit_index = k`;
- `created_at = v_created_at`, identical for the whole batch.

`RETURNING 1` is what makes the CTE's row count available to the next one. The arrays are read through `unnest`
rather than subscripted so the cost is linear in E. The two unique constraints on `event_log` and the unique commit id
index are checked as each row is inserted; given the lock, the head prefetch and the commit id probe they should never
fail, and if they ever do it indicates a write path that bypassed this function, and the whole batch rolls back.

### Lines 225–229: `advanced`

```sql
advanced AS (
    UPDATE __schema__.sequences AS s
    SET next = v_position_start + (SELECT count(*) FROM inserted)
    WHERE s.name = c_sequence_name AND (SELECT count(*) FROM inserted) > 0
)
```

Advances the sequence by exactly the number of rows inserted ([UPDATE](https://www.postgresql.org/docs/17/sql-update.html)).
Conflicts and duplicates consumed no positions, hence no gaps. The `WHERE` clause leaves the row untouched when
nothing was inserted, so a batch of pure conflicts does not create a dead tuple on the hot row.

This is the same row that was locked at line 107, so the `UPDATE` does not block. With `fillfactor = 50` and no
indexed column changing, it should be a HOT update, keeping the primary-key index untouched.

### Lines 230–238: the result rows

```sql
SELECT
    (d.ord - 1)::integer,
    d.req_status,
    (CASE WHEN d.observed < 0 THEN 0 ELSE 1 END)::smallint,
    CASE WHEN d.observed < 0 THEN NULL ELSE d.observed END,
    CASE WHEN d.req_status = 0 THEN d.first_pos END,
    CASE WHEN d.req_status = 0 THEN d.first_pos + d.event_count - 1 END
FROM decided AS d
ORDER BY d.ord;
```

One row per request in batch order, mapped to the protocol: 0-based `request_index`, the status, the observed head
translated into kind plus version, and the position range only for appended requests. The casts make the column
types match `RETURNS TABLE` exactly, which `RETURN QUERY` requires.

### Lines 240–241: end

```sql
RETURN;
END
```

Ends the function and hands the accumulated result rows to the caller. The transaction then commits and the row lock
is released; only now can the next waiting batch's `SELECT ... FOR UPDATE` proceed and re-read `next`.

## 6. Worked example

Batch of four requests, sequence row `next = 100`, stream `A` has events 0..2 (head 2), stream `B` does not exist.

| ord | stream | kind | version | commit | events |
|---|---|---|---|---|---|
| 1 | A | 3 AtVersion | 2 | c1 | 2 |
| 2 | B | 1 DoesNotExist | | c2 | 1 |
| 3 | A | 3 AtVersion | 2 | c3 | 1 |
| 4 | B | 0 Any | | c2 | 3 |

`request` adds `nth`, `duplicate` and `event_offset`:

| ord | nth | duplicate | event_offset |
|---|---|---|---|
| 1 | 1 | no | 1 |
| 2 | 1 | no | 3 |
| 3 | 2 | no | 4 |
| 4 | 2 | yes (c2 seen at ord 2) | 5 |

`head` yields `A → 2`, `B → -1`. `chain` then runs two iterations:

| iteration | stream | request | head seen | decision | head after |
|---|---|---|---|---|---|
| 1 | A | ord 1 | 2 | 2 = 2 ✔ Appended | 4 |
| 1 | B | ord 2 | −1 | absent ✔ Appended | 0 |
| 2 | A | ord 3 | 4 | 4 = 2 ✘ Conflict, observed AtVersion 4 | 4 |
| 2 | B | ord 4 | 0 | duplicate → Duplicate | 0 |

`decided` assigns positions in `ord` order over the accepted rows: ord 1 gets 100–101, ord 2 gets 102. The insert
produces three rows (positions 100, 101, 102; stream positions A3, A4, B0), and the sequence row becomes `next =
103`. Request 3 sees the head as if request 1 had already committed, which it will have by the time anyone can
observe it.

## 7. Correctness properties and why they hold

**Global positions are gap-free and in commit order.** The only writer of `sequences.next` is the `advanced` CTE,
executed under the row lock taken at line 107 and held to commit. A batch that raises never reaches it and rolls back
its inserts, so the counter only moves when rows are committed. Waiting batches re-read the committed value under READ
COMMITTED (guarded at line 54).

**Stream positions are contiguous per stream.** The head is read under the lock in a statement after the one that
took it, so it includes every committed row. Within a batch the recursion advances it as requests are accepted, so
consecutive requests to one stream chain correctly. No other writer can change a head while the lock is held.

**Expectations are checked against the true head.** Same reasoning; `c.head` at iteration n is the exact value that
will exist once the first n − 1 requests to that stream have been applied.

**Retrying a batch is safe.** If the client's statement fails after the server committed (for example, the connection
drops while the result set is in flight), the retry finds every commit id in `event_log` (line 121) and reports each
request as Duplicate. Requests that conflicted the first time still conflict, because the head has not moved for them.

**One result row per request.** Every request has exactly one `nth`, so it appears in exactly one iteration of
`chain` and therefore exactly once in `decided`. `AppendBatchCommand` still checks for missing rows defensively.

**Partial batches are never visible.** All writes happen in one transaction; any `RAISE` or constraint failure rolls
everything back, and the accepted/conflict/duplicate results of that batch are discarded with it.

## 8. Cost model: where the time goes

For a batch of R requests, S distinct streams, and E events, of which E′ are accepted:

| Step | Lines | Table access | Cost driver |
|---|---|---|---|
| Argument validation | 60–99 | none | O(R) in one statement over the arrays. |
| Lock | 107 | 1 row of `sequences` | Wait time behind the previous batch. This is the serialisation point. |
| Commit-id probe | 122 | index scan on `commit_id` with R keys | O(R log N) index probes, normally all misses. |
| `request` | 129 | none | Up to three sorts of R rows for the window functions. |
| `head` | 153 | S backwards index probes on `(stream_id, stream_position)` | O(S log N), independent of stream length. |
| `chain` | 164 | none | One join per iteration; iterations = max requests to one stream, usually 1. |
| `inserted` | 200 | E′ heap inserts, index entries: 2 per event + 1 per request | O(E′ log N) plus WAL volume proportional to payload size. |
| `advanced` | 225 | 1 HOT update | Constant. |
| Commit | | | WAL flush (`fsync`), a fixed cost per batch. |

Measured on `postgres:17` under Docker Desktop (Ryzen AI MAX+ 395, September 2026) for 500 single-event requests to
distinct new streams with realistic 45-character stream ids, after the partial commit-id index and the `"C"`
collation: about 5.2 ms of server time per batch inside the function, of which the insert is roughly half and the
two kinds of index probe roughly a quarter; the previous loop-based version took about 6.4 ms on the same batch.
Batches of ten events per request cost about 18 ms, almost all of it the insert. The client round trip adds around
1.5 ms including the commit flush.

Things that are fixed per batch and therefore amortised by batching: lock acquisition, plan lookup, the validation
statement, the sorts in `request`, the sequence update and the commit's WAL flush, together about 2 ms. Things that
scale with the batch: the probes and the insert. Batch size beyond a few hundred makes no measurable difference to
throughput because the per-request part dominates.

When measuring, the natural questions are:

- How much of a batch's wall time is the commit flush versus the statements? (`synchronous_commit`, WAL settings and
  disk latency dominate this; see [Asynchronous Commit](https://www.postgresql.org/docs/17/wal-async-commit.html).)
- Does the head prefetch actually get the backwards index-scan-with-limit plan? Check with
  [`EXPLAIN (ANALYZE, BUFFERS)`](https://www.postgresql.org/docs/17/using-explain.html) on the statement in isolation,
  or with the [`auto_explain`](https://www.postgresql.org/docs/17/auto-explain.html) module to capture plans from
  inside the function.
- How does the function compare with the whole round trip? `track_functions = pl` makes
  [`pg_stat_user_functions`](https://www.postgresql.org/docs/17/monitoring-stats.html#MONITORING-PG-STAT-USER-FUNCTIONS-VIEW)
  report calls and total time per function; the batch size sweep benchmark prints that next to the wall time per
  batch.
- How long do waiters spend blocked on the sequence row under concurrent load?
  [`pg_stat_activity`](https://www.postgresql.org/docs/17/monitoring-stats.html#MONITORING-PG-STAT-ACTIVITY-VIEW)
  shows `wait_event_type = Lock` for them, and
  [`pg_locks`](https://www.postgresql.org/docs/17/view-pg-locks.html) shows the tuple lock.
