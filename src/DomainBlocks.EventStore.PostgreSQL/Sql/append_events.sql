-- Appends a batch of requests in one transaction. The __schema__ token is replaced with the validated schema name.
--
-- Requests are described by parallel arrays of length R; their events by parallel arrays of length E = sum(event
-- counts), flattened in request order. All appenders serialize on the event_log sequence row, whose lock is held
-- until commit, so global positions are assigned in commit order without gaps.
--
-- Each request is evaluated independently: a conflict or duplicate is reported as a result row and does not abort
-- the batch. Only protocol violations (mismatched arrays, invalid kinds) raise, which faults the whole batch.
--
-- The batch is committed with a fixed number of statements regardless of its size, mirroring the MongoDB appender
-- policy: one probe for commit ids that already exist, one prefetch of the head of every stream in the batch, an
-- in-memory pass over the requests that keeps the heads up to date as it assigns positions (so repeated streams chain
-- correctly), then one insert of every accepted event and one update of the sequence row.
--
-- Codes:
--   expected kind: 0 Any, 1 DoesNotExist, 2 Exists, 3 AtVersion
--   status:        0 Appended, 1 Conflict, 2 Duplicate
--   observed kind: 0 DoesNotExist, 1 AtVersion
CREATE OR REPLACE FUNCTION __schema__.append_events(
    p_stream_ids        text[],
    p_expected_kinds    smallint[],
    p_expected_versions bigint[],
    p_commit_ids        uuid[],
    p_event_counts      integer[],
    p_event_names       text[],
    p_event_data        jsonb[],
    p_event_data_bytes  bytea[],
    p_metadata          jsonb[])
RETURNS TABLE (
    request_index    integer,
    status           smallint,
    observed_kind    smallint,
    observed_version bigint,
    first_position   bigint,
    last_position    bigint)
LANGUAGE plpgsql
SET plan_cache_mode = force_generic_plan
AS $fn$
DECLARE
    c_sequence_name    CONSTANT text := 'event_log';

    v_request_count    integer;
    v_event_total      bigint;
    v_position_start   bigint;
    v_position         bigint;
    v_created_at       timestamptz;
    v_existing_commits uuid[];
    v_heads            bigint[]; -- head stream position per distinct stream in the batch (-1 if absent), indexed by
                                 -- the stream's rank in stream id order, matching sid in the request loop
    v_head             bigint;
    v_matches          boolean;

    -- Accepted requests, in request order, for the single insert at the end.
    v_accepted         integer := 0;
    v_acc_index        integer[] := '{}';  -- 1-based index into the request arrays
    v_acc_first_pos    bigint[] := '{}';   -- global position of the request's first event
    v_acc_first_ver    bigint[] := '{}';   -- stream position of the request's first event
    v_acc_event_offset bigint[] := '{}';   -- 1-based offset into the event arrays
    v_acc_event_count  integer[] := '{}';

    v_req              record;
BEGIN
    -- The blocking SELECT ... FOR UPDATE below re-reads the newest committed row after waiting, which is READ
    -- COMMITTED behaviour. Under REPEATABLE READ or SERIALIZABLE the same wait would end in a serialization failure.
    IF current_setting('transaction_isolation') <> 'read committed' THEN
        RAISE EXCEPTION 'append_events requires READ COMMITTED isolation (current: %)',
            current_setting('transaction_isolation')
            USING ERRCODE = 'invalid_transaction_state';
    END IF;

    v_request_count := coalesce(cardinality(p_stream_ids), 0);

    IF v_request_count = 0 THEN
        RETURN;
    END IF;

    IF coalesce(cardinality(p_expected_kinds), 0) <> v_request_count OR
       coalesce(cardinality(p_expected_versions), 0) <> v_request_count OR
       coalesce(cardinality(p_commit_ids), 0) <> v_request_count OR
       coalesce(cardinality(p_event_counts), 0) <> v_request_count THEN
        RAISE EXCEPTION 'request arrays must all have length %', v_request_count
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF EXISTS (SELECT 1 FROM unnest(p_event_counts) AS c WHERE c IS NULL OR c <= 0) THEN
        RAISE EXCEPTION 'event counts must be positive'
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    SELECT sum(c) INTO v_event_total FROM unnest(p_event_counts) AS c;

    IF coalesce(cardinality(p_event_names), 0) <> v_event_total OR
       coalesce(cardinality(p_event_data), 0) <> v_event_total OR
       coalesce(cardinality(p_event_data_bytes), 0) <> v_event_total OR
       coalesce(cardinality(p_metadata), 0) <> v_event_total THEN
        RAISE EXCEPTION 'event arrays must all have length %', v_event_total
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids)
            AS r(stream_id, kind, version, commit_id)
        WHERE r.stream_id IS NULL OR r.stream_id = ''
           OR r.commit_id IS NULL
           OR r.kind IS NULL OR r.kind NOT BETWEEN 0 AND 3
           OR (r.kind = 3 AND (r.version IS NULL OR r.version < 0))
           OR (r.kind <> 3 AND r.version IS NOT NULL)) THEN
        RAISE EXCEPTION 'invalid request: check stream ids, commit ids, expected kinds and versions'
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    -- Serialize all appenders. This blocks until any in-flight batch has committed or rolled back.
    SELECT s.next INTO v_position_start
    FROM __schema__.sequences AS s
    WHERE s.name = c_sequence_name
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'sequence row "%" not found; the schema has not been initialized', c_sequence_name
            USING ERRCODE = 'undefined_object';
    END IF;

    v_position := v_position_start;
    v_created_at := clock_timestamp(); -- After the lock, so that it is monotone with position.

    -- Idempotency: one index probe for every commit id in the batch. Stable while the lock is held.
    v_existing_commits := ARRAY(
        SELECT DISTINCT e.commit_id
        FROM __schema__.event_log AS e
        WHERE e.commit_id = ANY (p_commit_ids));

    -- Heads: one index probe per distinct stream. The lateral max() lets the planner use the (stream_id,
    -- stream_position) index backwards with a limit, so each probe is O(1) however long the stream is.
    SELECT coalesce(array_agg(coalesce(h.head, -1) ORDER BY s.stream_id), '{}'::bigint[]) INTO v_heads
    FROM (SELECT DISTINCT d.stream_id FROM unnest(p_stream_ids) AS d(stream_id)) AS s
    CROSS JOIN LATERAL (
        SELECT max(e.stream_position) AS head
        FROM __schema__.event_log AS e
        WHERE e.stream_id = s.stream_id) AS h;

    -- Evaluate every request in order without touching the tables. Positions are assigned here; heads are kept
    -- current so that a later request to the same stream observes the rows an earlier one will insert.
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
        request_index := v_req.idx - 1;
        first_position := NULL;
        last_position := NULL;

        v_head := v_heads[v_req.sid];
        observed_kind := CASE WHEN v_head < 0 THEN 0 ELSE 1 END;
        observed_version := CASE WHEN v_head < 0 THEN NULL ELSE v_head END;

        -- The first occurrence of a commit id in the batch wins; later ones are duplicates.
        IF v_req.commit_occurrence > 1 OR v_req.commit_id = ANY (v_existing_commits) THEN
            status := 2;
            RETURN NEXT;
            CONTINUE;
        END IF;

        v_matches := CASE v_req.kind
            WHEN 0 THEN true
            WHEN 1 THEN v_head < 0
            WHEN 2 THEN v_head >= 0
            WHEN 3 THEN v_head = v_req.version
        END;

        IF NOT v_matches THEN
            status := 1;
            RETURN NEXT;
            CONTINUE;
        END IF;

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
    END LOOP;

    IF v_accepted = 0 THEN
        RETURN;
    END IF;

    -- One insert for every accepted event. Arrays are read through unnest rather than subscripted, so the cost is
    -- linear in the batch size.
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

    -- Advance by exactly the number of rows inserted: conflicts and duplicates leave no gap.
    UPDATE __schema__.sequences AS s SET next = v_position WHERE s.name = c_sequence_name;

    RETURN;
END
$fn$;
