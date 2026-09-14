-- Appends a batch of requests in one transaction. The __schema__ token is replaced with the validated schema name.
--
-- Requests are described by parallel arrays of length R; their events by parallel arrays of length E = sum(event
-- counts), flattened in request order. All appenders serialize on the event_log sequence row, whose lock is held until
-- commit, so global positions are assigned in commit order without gaps.
--
-- Each request is evaluated independently: a conflict or duplicate is reported as a result row and does not abort the
-- batch. Only protocol violations (mismatched arrays, invalid kinds) raise, which faults the whole batch.
--
-- The batch is committed with a fixed number of statements regardless of its size: one validation pass over the
-- arrays, the lock, one probe for commit IDs that already exist, then a single statement that prefetches the head of
-- every stream in the batch, evaluates the requests, inserts every accepted event, advances the sequence row and
-- returns the result rows. Requests to the same stream chain through a recursive CTE that advances every stream one
-- request per iteration, carrying the running head, so a later request observes the rows an earlier one will insert.
-- A batch whose streams are all distinct completes in one iteration.
--
-- Codes:
--   expected kind: 0 Any, 1 DoesNotExist, 2 Exists, 3 AtVersion
--   status:        0 Appended, 1 Conflict, 2 Duplicate
--   observed kind: 0 DoesNotExist, 1 AtVersion

-- Decides one request against the head of its stream, where a head of -1 means the stream does not exist. A scalar
-- SQL function whose body is a single expression is inlined by the planner, so this costs nothing at run time; it
-- exists to keep the decision, and the codes it interprets, in one place.
CREATE OR REPLACE FUNCTION __schema__.append_status(
    p_duplicate boolean,
    p_expected_kind smallint,
    p_expected_version bigint,
    p_head bigint)
    RETURNS smallint
    LANGUAGE sql
    IMMUTABLE
AS
$fn$
SELECT CASE
           WHEN p_duplicate THEN 2
           WHEN p_expected_kind = 0 THEN 0
           WHEN p_expected_kind = 1 AND p_head < 0 THEN 0
           WHEN p_expected_kind = 2 AND p_head >= 0 THEN 0
           WHEN p_expected_kind = 3 AND p_head = p_expected_version THEN 0
           ELSE 1
       END::smallint
$fn$;

-- Rejects a batch that violates the protocol: request or event arrays of different lengths, an event count that is
-- not positive, or a request whose fields are missing or inconsistent. One pass over the request arrays computes the
-- event total and both validity checks.
CREATE OR REPLACE FUNCTION __schema__.validate_append_batch(
    p_stream_ids text[],
    p_expected_kinds smallint[],
    p_expected_versions bigint[],
    p_commit_ids uuid[],
    p_event_counts integer[],
    p_event_names text[],
    p_event_data jsonb[],
    p_event_data_bytes bytea[],
    p_metadata jsonb[])
    RETURNS void
    LANGUAGE plpgsql
    SET plan_cache_mode = force_generic_plan
AS
$fn$
DECLARE
    v_request_count   integer;
    v_event_total     bigint;
    v_invalid_count   boolean;
    v_invalid_request boolean;
BEGIN
    v_request_count := coalesce(cardinality(p_stream_ids), 0);

    IF coalesce(cardinality(p_expected_kinds), 0) <> v_request_count OR
       coalesce(cardinality(p_expected_versions), 0) <> v_request_count OR
       coalesce(cardinality(p_commit_ids), 0) <> v_request_count OR
       coalesce(cardinality(p_event_counts), 0) <> v_request_count THEN
        RAISE EXCEPTION 'request arrays must all have length %', v_request_count
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    SELECT coalesce(sum(r.event_count), 0),
           bool_or(r.event_count IS NULL OR r.event_count <= 0),
           bool_or(r.stream_id IS NULL OR r.stream_id = ''
               OR r.commit_id IS NULL
               OR r.kind IS NULL OR r.kind NOT BETWEEN 0 AND 3
               OR (r.kind = 3 AND (r.version IS NULL OR r.version < 0))
               OR (r.kind <> 3 AND r.version IS NOT NULL))
    INTO v_event_total, v_invalid_count, v_invalid_request
    FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids, p_event_counts)
             AS r(stream_id, kind, version, commit_id, event_count);

    IF v_invalid_count THEN
        RAISE EXCEPTION 'event counts must be positive'
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF coalesce(cardinality(p_event_names), 0) <> v_event_total OR
       coalesce(cardinality(p_event_data), 0) <> v_event_total OR
       coalesce(cardinality(p_event_data_bytes), 0) <> v_event_total OR
       coalesce(cardinality(p_metadata), 0) <> v_event_total THEN
        RAISE EXCEPTION 'event arrays must all have length %', v_event_total
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF v_invalid_request THEN
        RAISE EXCEPTION 'invalid request: check stream ids, commit ids, expected kinds and versions'
            USING ERRCODE = 'invalid_parameter_value';
    END IF;
END
$fn$;

-- The requests of a batch, one row each with the derived columns the append needs. A set-returning SQL function that
-- is a single SELECT, not volatile and not strict, is inlined by the planner as a subquery of the calling statement,
-- so this shapes the query without adding a function call or a plan boundary.
CREATE OR REPLACE FUNCTION __schema__.append_requests(
    p_stream_ids text[],
    p_expected_kinds smallint[],
    p_expected_versions bigint[],
    p_commit_ids uuid[],
    p_event_counts integer[],
    p_existing_commits uuid[])
    RETURNS TABLE
            (
                ord          bigint,
                stream_id    text,
                kind         smallint,
                version      bigint,
                commit_id    uuid,
                event_count  integer,
                nth          bigint,
                duplicate    boolean,
                event_offset bigint
            )
    LANGUAGE sql
    IMMUTABLE
AS
$fn$
SELECT r.ord,
       -- Unnest yields the database collation; "C" keeps the partition sort below byte-wise, like the column.
       r.stream_id COLLATE "C",
       r.kind,
       r.version,
       r.commit_id,
       r.event_count,
       -- Position of the request among the requests to its stream, in batch order.
       row_number() OVER (PARTITION BY r.stream_id COLLATE "C" ORDER BY r.ord),
       -- The first occurrence of a commit id in the batch wins; later ones and already committed ones are duplicates.
       row_number() OVER (PARTITION BY r.commit_id ORDER BY r.ord) > 1 OR r.commit_id = ANY (p_existing_commits),
       -- 1-based offset of the request's first event in the flattened event arrays.
       1 + coalesce(sum(r.event_count) OVER (ORDER BY r.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids, p_event_counts)
         WITH ORDINALITY AS r(stream_id, kind, version, commit_id, event_count, ord)
$fn$;

-- The head of every distinct stream in the batch, -1 if the stream has no events. The lateral max() lets the planner
-- use the (stream_id, stream_position) index backwards with a limit, so each probe is O(1) however long the stream
-- is. Inlined like append_requests.
CREATE OR REPLACE FUNCTION __schema__.stream_heads(p_stream_ids text[])
    RETURNS TABLE
            (
                stream_id text,
                head      bigint
            )
    LANGUAGE sql
    STABLE
AS
$fn$
SELECT s.stream_id, coalesce(h.head, -1)
FROM (SELECT DISTINCT u.stream_id COLLATE "C" AS stream_id FROM unnest(p_stream_ids) AS u(stream_id)) AS s
         CROSS JOIN LATERAL (SELECT max(e.stream_position) AS head
                             FROM __schema__.event_log AS e
                             WHERE e.stream_id = s.stream_id) AS h
$fn$;

CREATE OR REPLACE FUNCTION __schema__.append_events(
    p_stream_ids text[],
    p_expected_kinds smallint[],
    p_expected_versions bigint[],
    p_commit_ids uuid[],
    p_event_counts integer[],
    p_event_names text[],
    p_event_data jsonb[],
    p_event_data_bytes bytea[],
    p_metadata jsonb[])
    RETURNS TABLE
            (
                request_index    integer,
                status           smallint,
                observed_kind    smallint,
                observed_version bigint,
                first_position   bigint,
                last_position    bigint
            )
    LANGUAGE plpgsql
    SET plan_cache_mode = force_generic_plan
AS
$fn$
DECLARE
    c_sequence_name CONSTANT text := 'event_log';
    v_position_start         bigint;
    v_created_at             timestamptz;
    v_existing_commits       uuid[];
BEGIN
    -- The blocking SELECT ... FOR UPDATE below re-reads the newest committed row after waiting, which is READ COMMITTED
    -- behaviour. Under REPEATABLE READ or SERIALIZABLE the same wait would end in a serialization failure.
    IF current_setting('transaction_isolation') <> 'read committed' THEN
        RAISE EXCEPTION 'append_events requires READ COMMITTED isolation (current: %)',
            current_setting('transaction_isolation')
            USING ERRCODE = 'invalid_transaction_state';
    END IF;

    PERFORM __schema__.validate_append_batch(
            p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids, p_event_counts,
            p_event_names, p_event_data, p_event_data_bytes, p_metadata);

    -- An empty batch is valid once every array has been checked against it.
    IF coalesce(cardinality(p_stream_ids), 0) = 0 THEN
        RETURN;
    END IF;

    -- Serialize all appenders. This blocks until any in-flight batch has committed or rolled back.
    SELECT s.next
    INTO v_position_start
    FROM __schema__.sequences AS s
    WHERE s.name = c_sequence_name
        FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'sequence row "%" not found; the schema has not been initialized', c_sequence_name
            USING ERRCODE = 'undefined_object';
    END IF;

    -- Get timestamp after the lock, so that it is monotone with position.
    v_created_at := clock_timestamp();

    -- Idempotency: one index probe for every commit id in the batch. Stable while the lock is held. The commit id index
    -- is partial on commit_index = 0, which every request has exactly one row for, so the predicate is what lets the
    -- planner use it and also makes the result distinct.
    v_existing_commits := ARRAY(
            SELECT e.commit_id
            FROM __schema__.event_log AS e
            WHERE e.commit_id = ANY (p_commit_ids)
              AND e.commit_index = 0);

    -- Everything else is one statement. Its snapshot is taken after the lock, so the heads it reads are current.
    RETURN QUERY
        WITH RECURSIVE
            request AS (SELECT *
                        FROM __schema__.append_requests(p_stream_ids, p_expected_kinds, p_expected_versions,
                                                        p_commit_ids, p_event_counts, v_existing_commits)),
            -- Evaluate the requests, seeded with the head of every stream in the batch. Iteration n decides the n-th
            -- request of every stream against the head left by the previous n - 1, so a batch with no repeated stream
            -- needs one iteration. Streams are independent, so evaluating them side by side gives the same outcome as
            -- evaluating the batch in order.
            chain AS (SELECT h.stream_id,
                             0::bigint      AS nth,
                             h.head,
                             NULL::bigint   AS ord,
                             NULL::smallint AS req_status,
                             NULL::bigint   AS observed
                      FROM __schema__.stream_heads(p_stream_ids) AS h
                      UNION ALL
                      SELECT r.stream_id,
                             r.nth,
                             CASE WHEN d.req_status = 0 THEN c.head + r.event_count ELSE c.head END,
                             r.ord,
                             d.req_status,
                             c.head
                      FROM chain AS c
                               JOIN request AS r ON r.stream_id = c.stream_id AND r.nth = c.nth + 1
                               CROSS JOIN LATERAL (
                                   SELECT __schema__.append_status(r.duplicate, r.kind, r.version, c.head)) AS d(req_status)),
            -- Global positions are contiguous over appended requests, in batch order.
            decided AS (SELECT r.ord,
                               r.stream_id,
                               r.commit_id,
                               r.event_count,
                               r.event_offset,
                               c.req_status,
                               c.observed,
                               v_position_start +
                               coalesce(sum(r.event_count)
                                        FILTER (WHERE c.req_status = 0)
                                            OVER (ORDER BY r.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),
                                        0) AS first_pos
                        FROM chain AS c
                                 JOIN request AS r ON r.ord = c.ord),
            -- One insert for every accepted event. Arrays are read through unnest rather than subscripted, so the cost
            -- is linear in the batch size.
            inserted AS (
                INSERT INTO __schema__.event_log (position, stream_id, stream_position, commit_id, commit_index,
                                                  event_name, event_data, event_data_bytes, metadata, created_at)
                    SELECT d.first_pos + g.k,
                           d.stream_id,
                           d.observed + 1 + g.k,
                           d.commit_id,
                           g.k,
                           ev.event_name,
                           ev.event_data,
                           ev.event_data_bytes,
                           ev.metadata,
                           v_created_at
                    FROM decided AS d
                             CROSS JOIN LATERAL generate_series(0, d.event_count - 1) AS g(k)
                             JOIN unnest(p_event_names, p_event_data, p_event_data_bytes, p_metadata)
                        WITH ORDINALITY AS ev(event_name, event_data, event_data_bytes, metadata, ord)
                                  ON ev.ord = d.event_offset + g.k
                    WHERE d.req_status = 0
                    RETURNING 1),
            -- Advance by exactly the number of rows inserted: conflicts and duplicates leave no gap, and a batch that
            -- appended nothing leaves the row untouched.
            advanced AS (
                UPDATE __schema__.sequences AS s
                    SET next = v_position_start + (SELECT count(*) FROM inserted)
                    WHERE s.name = c_sequence_name AND (SELECT count(*) FROM inserted) > 0)
        SELECT (d.ord - 1)::integer,
               d.req_status,
               (CASE WHEN d.observed < 0 THEN 0 ELSE 1 END)::smallint,
               CASE WHEN d.observed < 0 THEN NULL ELSE d.observed END,
               CASE WHEN d.req_status = 0 THEN d.first_pos END,
               CASE WHEN d.req_status = 0 THEN d.first_pos + d.event_count - 1 END
        FROM decided AS d
        ORDER BY d.ord;

    RETURN;
END
$fn$;
