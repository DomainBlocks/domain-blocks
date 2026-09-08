-- Appends a batch of requests in one transaction. The __schema__ token is replaced with the validated schema name.
--
-- Requests are described by parallel arrays of length R; their events by parallel arrays of length E = sum(event
-- counts), flattened in request order. All appenders serialize on the event_log sequence row, whose lock is held
-- until commit, so global positions are assigned in commit order without gaps.
--
-- Each request is evaluated independently: a conflict or duplicate is reported as a result row and does not abort
-- the batch. Only protocol violations (mismatched arrays, invalid kinds) raise, which faults the whole batch.
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
AS $fn$
DECLARE
    c_sequence_name    CONSTANT text := 'event_log';

    v_request_count    integer;
    v_event_total      bigint;
    v_position_start   bigint;
    v_position         bigint;
    v_created_at       timestamptz;
    v_existing_commits uuid[];
    v_seen_commits     uuid[] := '{}';

    v_stream_id        text;
    v_expected_kind    smallint;
    v_expected_version bigint;
    v_commit_id        uuid;
    v_event_count      integer;
    v_event_offset     integer := 1;
    v_head             bigint;
    v_matches          boolean;
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

    FOR i IN 1 .. v_request_count LOOP
        v_stream_id := p_stream_ids[i];
        v_expected_kind := p_expected_kinds[i];
        v_expected_version := p_expected_versions[i];
        v_commit_id := p_commit_ids[i];
        v_event_count := p_event_counts[i];

        request_index := i - 1;
        first_position := NULL;
        last_position := NULL;

        -- Sees rows inserted by earlier requests in this batch, so repeated appends to one stream chain correctly.
        SELECT max(e.stream_position) INTO v_head
        FROM __schema__.event_log AS e
        WHERE e.stream_id = v_stream_id;

        observed_kind := CASE WHEN v_head IS NULL THEN 0 ELSE 1 END;
        observed_version := v_head;

        IF v_commit_id = ANY (v_existing_commits) OR v_commit_id = ANY (v_seen_commits) THEN
            status := 2;
            RETURN NEXT;
            v_event_offset := v_event_offset + v_event_count;
            CONTINUE;
        END IF;

        v_seen_commits := v_seen_commits || v_commit_id;

        v_matches := CASE v_expected_kind
            WHEN 0 THEN true
            WHEN 1 THEN v_head IS NULL
            WHEN 2 THEN v_head IS NOT NULL
            WHEN 3 THEN v_head IS NOT NULL AND v_head = v_expected_version
        END;

        IF NOT v_matches THEN
            status := 1;
            RETURN NEXT;
            v_event_offset := v_event_offset + v_event_count;
            CONTINUE;
        END IF;

        INSERT INTO __schema__.event_log (
            position, stream_id, stream_position, commit_id, commit_index,
            event_name, event_data, event_data_bytes, metadata, created_at)
        SELECT
            v_position + g.k,
            v_stream_id,
            coalesce(v_head, -1) + 1 + g.k,
            v_commit_id,
            g.k,
            p_event_names[v_event_offset + g.k],
            p_event_data[v_event_offset + g.k],
            p_event_data_bytes[v_event_offset + g.k],
            p_metadata[v_event_offset + g.k],
            v_created_at
        FROM generate_series(0, v_event_count - 1) AS g(k);

        status := 0;
        first_position := v_position;
        last_position := v_position + v_event_count - 1;
        RETURN NEXT;

        v_position := v_position + v_event_count;
        v_event_offset := v_event_offset + v_event_count;
    END LOOP;

    -- Advance by exactly the number of rows inserted: conflicts and duplicates leave no gap.
    IF v_position <> v_position_start THEN
        UPDATE __schema__.sequences AS s SET next = v_position WHERE s.name = c_sequence_name;
    END IF;

    RETURN;
END
$fn$;
