-- Appends a batch of requests in one transaction. __schema__ is replaced with the validated schema name.
--
-- Requests are passed as parallel arrays of length R. Their events are passed as parallel arrays of length E, the sum
-- of the event counts, flattened in request order. Appenders serialize on the event_log sequence row, which stays
-- locked until commit, so global positions follow commit order without gaps.
--
-- A conflict or duplicate is reported as a result row and does not abort the batch. Only a protocol violation raises an
-- error, which fails the whole batch.
--
-- Every batch takes the same number of statements, whatever its size: validation, the lock, one probe for existing
-- commit IDs, and one statement that reads each stream's head, evaluates the requests, inserts the accepted events,
-- advances the sequence row, and returns the results. Requests to the same stream chain through a recursive CTE that
-- handles one request per stream in each iteration, so a batch of distinct streams needs a single iteration.
--
-- The expected kinds and statuses are the expected_state_kind and append_status enums from schema.sql.

CREATE OR REPLACE FUNCTION __schema__.append_events(
    p_stream_ids text[],
    p_expected_kinds __schema__.expected_state_kind[],
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
                status           __schema__.append_status,
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
    -- Only READ COMMITTED lets the blocking SELECT ... FOR UPDATE below re-read the newest row after waiting. Stricter
    -- levels end the wait with a serialization failure.
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

    -- This serializes appenders. It blocks until any in-flight batch commits or rolls back.
    SELECT s.next
    INTO v_position_start
    FROM __schema__.sequences AS s
    WHERE s.name = c_sequence_name
        FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'sequence row "%" not found; the schema has not been initialized', c_sequence_name
            USING ERRCODE = 'undefined_object';
    END IF;

    -- The timestamp is taken after the lock, so timestamps follow position order unless the system clock moves
    -- backward.
    v_created_at := clock_timestamp();

    -- One probe finds every commit ID in the batch that already exists, and the result cannot change while the lock is
    -- held. The index is partial on commit_index = 0, which matches one row per request, so the predicate lets the
    -- planner use the index and makes the result distinct.
    v_existing_commits := ARRAY(
            SELECT e.commit_id
            FROM __schema__.event_log AS e
            WHERE e.commit_id = ANY (p_commit_ids)
              AND e.commit_index = 0);

    -- Everything else is one statement. Its snapshot is taken after the lock, so the heads it reads are current.
    RETURN QUERY
        WITH RECURSIVE
            request AS (SELECT *
                        FROM __schema__.zip_requests(p_stream_ids,
                                                     p_expected_kinds,
                                                     p_expected_versions,
                                                     p_commit_ids,
                                                     p_event_counts,
                                                     v_existing_commits)),
            -- The seed row holds each stream's current head. Iteration n decides the n-th request of every stream
            -- against the head that the previous request left. Streams are independent, so this gives the same outcome
            -- as evaluating the batch in order.
            chain AS (SELECT h.stream_id,
                             0::bigint                      AS nth,
                             NULL::bigint                   AS ord,
                             NULL::bigint                   AS head_before,
                             NULL::__schema__.append_status AS status,
                             h.head                         AS head_after
                      FROM __schema__.get_stream_heads(p_stream_ids) AS h
                      UNION ALL
                      SELECT r.stream_id,
                             r.nth,
                             r.ord,
                             c.head_after,
                             d.status,
                             CASE WHEN d.status = 'appended' THEN c.head_after + r.event_count ELSE c.head_after END
                      FROM chain AS c
                               JOIN request AS r ON r.stream_id = c.stream_id AND r.nth = c.nth + 1
                               CROSS JOIN LATERAL (SELECT __schema__.get_append_status(r.duplicate,
                                                                                       r.expected_kind,
                                                                                       r.expected_version,
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
                                        FILTER (WHERE c.status = 'appended')
                                            OVER (ORDER BY r.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),
                                        0) AS first_pos
                        FROM chain AS c
                                 JOIN request AS r ON r.ord = c.ord),
            -- One insert for every accepted event. The arrays are read through unnest rather than by subscript, so the
            -- cost stays linear in the batch size.
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
                    WHERE d.status = 'appended'
                    RETURNING 1),
            -- The sequence advances by exactly the number of rows inserted. Conflicts and duplicates leave no gap, and
            -- a batch that appended nothing leaves the row untouched.
            advanced AS (
                UPDATE __schema__.sequences AS s
                    SET next = v_position_start + (SELECT count(*) FROM inserted)
                    WHERE s.name = c_sequence_name AND (SELECT count(*) FROM inserted) > 0)
        -- A head of -1 means the stream did not exist, reported as a NULL observed version.
        SELECT (d.ord - 1)::integer                                                     AS request_index,
               d.status,
               nullif(d.head_before, -1)                                                AS observed_version,
               CASE WHEN d.status = 'appended' THEN d.first_pos END                     AS first_position,
               CASE WHEN d.status = 'appended' THEN d.first_pos + d.event_count - 1 END AS last_position
        FROM decided AS d
        ORDER BY d.ord;

    RETURN;
END
$fn$;
