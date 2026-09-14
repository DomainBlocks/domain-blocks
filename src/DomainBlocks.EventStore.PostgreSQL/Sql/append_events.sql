-- Appends a batch of requests in one transaction. The __schema__ token is replaced with the validated schema name.
--
-- Requests are described by parallel arrays of length R; their events by parallel arrays of length E = sum(event
-- counts), flattened in request order. All appenders serialize on the event_log sequence row, whose lock is held until
-- commit, so global positions are assigned in commit order without gaps.
--
-- Each request is evaluated independently: a conflict or duplicate is reported as a result row and does not abort the
-- batch. Only protocol violations (mismatched arrays, invalid kinds) raise, which faults the whole batch.
--
-- The batch is committed with a fixed number of statements regardless of its size: one validation pass over the arrays,
-- the lock, one probe for commit IDs that already exist, then a single statement that prefetches the head of every
-- stream in the batch, evaluates the requests, inserts every accepted event, advances the sequence row and returns the
-- result rows. Requests to the same stream chain through a recursive CTE that advances every stream one request per
-- iteration, carrying the running head, so a later request observes the rows an earlier one will insert. A batch whose
-- streams are all distinct completes in one iteration.
--
-- Codes:
--   expected kind: 0 Any, 1 DoesNotExist, 2 Exists, 3 AtVersion
--   status:        0 Appended, 1 Conflict, 2 Duplicate
--   observed kind: 0 DoesNotExist, 1 AtVersion

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
            p_stream_ids,
            p_expected_kinds,
            p_expected_versions,
            p_commit_ids,
            p_event_counts,
            p_event_names,
            p_event_data,
            p_event_data_bytes,
            p_metadata);

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
                        FROM __schema__.unnest_requests(p_stream_ids,
                                                        p_expected_kinds,
                                                        p_expected_versions,
                                                        p_commit_ids,
                                                        p_event_counts,
                                                        v_existing_commits)),
            -- Evaluate the requests, carrying the head of each stream forward. The seed row of a stream holds its
            -- current head; iteration n decides the n-th request of every stream against the head left by the previous
            -- n - 1, so a batch with no repeated stream needs one iteration. Streams are independent, so evaluating
            -- them side by side gives the same outcome as evaluating the batch in order.
            chain AS (SELECT h.stream_id,
                             0::bigint      AS nth,
                             NULL::bigint   AS ord,
                             NULL::bigint   AS head_before,
                             NULL::smallint AS status,
                             h.head         AS head_after
                      FROM __schema__.get_stream_heads(p_stream_ids) AS h
                      UNION ALL
                      SELECT r.stream_id,
                             r.nth,
                             r.ord,
                             c.head_after,
                             d.status,
                             CASE WHEN d.status = 0 THEN c.head_after + r.event_count ELSE c.head_after END
                      FROM chain AS c
                               JOIN request AS r ON r.stream_id = c.stream_id AND r.nth = c.nth + 1
                               CROSS JOIN LATERAL (SELECT __schema__.get_append_status(r.duplicate,
                                                                                       r.kind,
                                                                                       r.version,
                                                                                       c.head_after)) AS d(status)),
            -- Global positions are contiguous over appended requests, in batch order.
            decided AS (SELECT r.ord,
                               r.stream_id,
                               r.commit_id,
                               r.event_count,
                               r.event_offset,
                               c.status,
                               c.head_before,
                               v_position_start +
                               coalesce(sum(r.event_count)
                                        FILTER (WHERE c.status = 0)
                                            OVER (ORDER BY r.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),
                                        0) AS first_pos
                        FROM chain AS c
                                 JOIN request AS r ON r.ord = c.ord),
            -- One insert for every accepted event. Arrays are read through unnest rather than subscripted, so the cost
            -- is linear in the batch size.
            inserted AS (
                INSERT INTO __schema__.event_log (position,
                                                  stream_id,
                                                  stream_position,
                                                  commit_id,
                                                  commit_index,
                                                  event_name,
                                                  event_data,
                                                  event_data_bytes,
                                                  metadata,
                                                  created_at)
                    SELECT d.first_pos + g.k,
                           d.stream_id,
                           d.head_before + 1 + g.k,
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
                    WHERE d.status = 0
                    RETURNING 1),
            -- Advance by exactly the number of rows inserted: conflicts and duplicates leave no gap, and a batch that
            -- appended nothing leaves the row untouched.
            advanced AS (
                UPDATE __schema__.sequences AS s
                    SET next = v_position_start + (SELECT count(*) FROM inserted)
                    WHERE s.name = c_sequence_name AND (SELECT count(*) FROM inserted) > 0)
        -- The head before the request is what the caller observed: -1 reports as DoesNotExist with no version.
        SELECT (d.ord - 1)::integer                                            AS request_index,
               d.status,
               (CASE WHEN d.head_before < 0 THEN 0 ELSE 1 END)::smallint       AS observed_kind,
               nullif(d.head_before, -1)                                       AS observed_version,
               CASE WHEN d.status = 0 THEN d.first_pos END                     AS first_position,
               CASE WHEN d.status = 0 THEN d.first_pos + d.event_count - 1 END AS last_position
        FROM decided AS d
        ORDER BY d.ord;

    RETURN;
END
$fn$;
