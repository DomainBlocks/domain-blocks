-- Helpers of append_events. The __schema__ token is replaced with the validated schema name. Each helper is either
-- inlined by the planner into the statement that calls it, or called once per batch, so the split costs nothing at
-- run time. The enums the helpers take and return are created in schema.sql.

-- Earlier versions expressed the codes as smallint. CREATE OR REPLACE cannot change a function's return type, and the
-- old overloads would otherwise stay callable, so they are dropped first.
DROP FUNCTION IF EXISTS __schema__.append_events(text[], smallint[], bigint[], uuid[], integer[], text[], jsonb[], bytea[], jsonb[]);
DROP FUNCTION IF EXISTS __schema__.validate_append_batch(text[], smallint[], bigint[], uuid[], integer[], text[], jsonb[], bytea[], jsonb[]);
DROP FUNCTION IF EXISTS __schema__.zip_requests(text[], smallint[], bigint[], uuid[], integer[], uuid[]);
DROP FUNCTION IF EXISTS __schema__.get_append_status(boolean, smallint, bigint, bigint);

-- Rejects a batch that violates the protocol: request or event arrays of different lengths, an event count that is
-- not positive, or a request whose fields are missing or inconsistent. One pass over the request arrays computes the
-- event total and, for each rule, the first request that breaks it, so the error names the request by the 0-based
-- index the caller used.
CREATE OR REPLACE FUNCTION __schema__.validate_append_batch(
    p_stream_ids text[],
    p_expected_kinds __schema__.expected_state_kind[],
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
    v_request_count     integer;
    v_event_total       bigint;
    v_bad_event_count   bigint;
    v_bad_stream_id     bigint;
    v_bad_commit_id     bigint;
    v_bad_expected_kind bigint;
    v_missing_version   bigint;
    v_stray_version     bigint;
BEGIN
    v_request_count := coalesce(cardinality(p_stream_ids), 0);

    IF coalesce(cardinality(p_expected_kinds), 0) <> v_request_count OR
       coalesce(cardinality(p_expected_versions), 0) <> v_request_count OR
       coalesce(cardinality(p_commit_ids), 0) <> v_request_count OR
       coalesce(cardinality(p_event_counts), 0) <> v_request_count THEN
        RAISE EXCEPTION 'request arrays must all have length %, the length of p_stream_ids', v_request_count
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    -- The ordinal of the first request that breaks each rule, NULL if none does.
    SELECT coalesce(sum(r.event_count), 0),
           min(r.ord) FILTER (WHERE r.event_count IS NULL OR r.event_count <= 0),
           min(r.ord) FILTER (WHERE r.stream_id IS NULL OR r.stream_id = ''),
           min(r.ord) FILTER (WHERE r.commit_id IS NULL),
           min(r.ord) FILTER (WHERE r.expected_kind IS NULL),
           min(r.ord) FILTER (WHERE r.expected_kind = 'at_version' AND
                                    (r.expected_version IS NULL OR r.expected_version < 0)),
           min(r.ord) FILTER (WHERE r.expected_kind <> 'at_version' AND r.expected_version IS NOT NULL)
    INTO v_event_total,
        v_bad_event_count,
        v_bad_stream_id,
        v_bad_commit_id,
        v_bad_expected_kind,
        v_missing_version,
        v_stray_version
    FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids, p_event_counts)
             WITH ORDINALITY AS r(stream_id, expected_kind, expected_version, commit_id, event_count, ord);

    IF v_bad_event_count IS NOT NULL THEN
        RAISE EXCEPTION 'request %: event count must be positive', v_bad_event_count - 1
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF coalesce(cardinality(p_event_names), 0) <> v_event_total OR
       coalesce(cardinality(p_event_data), 0) <> v_event_total OR
       coalesce(cardinality(p_event_data_bytes), 0) <> v_event_total OR
       coalesce(cardinality(p_metadata), 0) <> v_event_total THEN
        RAISE EXCEPTION 'event arrays must all have length %, the sum of p_event_counts', v_event_total
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF v_bad_stream_id IS NOT NULL THEN
        RAISE EXCEPTION 'request %: stream id must not be null or empty', v_bad_stream_id - 1
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF v_bad_commit_id IS NOT NULL THEN
        RAISE EXCEPTION 'request %: commit id must not be null', v_bad_commit_id - 1
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF v_bad_expected_kind IS NOT NULL THEN
        RAISE EXCEPTION 'request %: expected kind must not be null', v_bad_expected_kind - 1
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF v_missing_version IS NOT NULL THEN
        RAISE EXCEPTION 'request %: expected version must be non-negative when expected kind is ''at_version''',
            v_missing_version - 1
            USING ERRCODE = 'invalid_parameter_value';
    END IF;

    IF v_stray_version IS NOT NULL THEN
        RAISE EXCEPTION 'request %: expected version must be null unless expected kind is ''at_version''',
            v_stray_version - 1
            USING ERRCODE = 'invalid_parameter_value';
    END IF;
END
$fn$;

-- Decides one request against the head of its stream, where a head of -1 means the stream does not exist. A scalar
-- SQL function whose body is a single expression is inlined by the planner, so this costs nothing at run time; it
-- exists to keep the decision in one place.
CREATE OR REPLACE FUNCTION __schema__.get_append_status(
    p_duplicate boolean,
    p_expected_kind __schema__.expected_state_kind,
    p_expected_version bigint,
    p_head bigint)
    RETURNS __schema__.append_status
    LANGUAGE sql
    IMMUTABLE
AS
$fn$
-- The cast on the first branch types the CASE; the other labels resolve to the same enum.
SELECT CASE
           WHEN p_duplicate THEN 'duplicate'::__schema__.append_status
           WHEN p_expected_kind = 'any' THEN 'appended'
           WHEN p_expected_kind = 'does_not_exist' AND p_head < 0 THEN 'appended'
           WHEN p_expected_kind = 'exists' AND p_head >= 0 THEN 'appended'
           WHEN p_expected_kind = 'at_version' AND p_head = p_expected_version THEN 'appended'
           ELSE 'conflict'
           END
$fn$;

-- The requests of a batch, one row each with the derived columns the append needs. A set-returning SQL function that
-- is a single SELECT, not volatile and not strict, is inlined by the planner as a subquery of the calling statement,
-- so this shapes the query without adding a function call or a plan boundary.
CREATE OR REPLACE FUNCTION __schema__.zip_requests(
    p_stream_ids text[],
    p_expected_kinds __schema__.expected_state_kind[],
    p_expected_versions bigint[],
    p_commit_ids uuid[],
    p_event_counts integer[],
    p_existing_commits uuid[])
    RETURNS TABLE
            (
                ord              bigint,
                stream_id        text,
                expected_kind    __schema__.expected_state_kind,
                expected_version bigint,
                commit_id        uuid,
                event_count      integer,
                nth              bigint,
                duplicate        boolean,
                event_offset     bigint
            )
    LANGUAGE sql
    IMMUTABLE
AS
$fn$
SELECT r.ord,
       -- Unnest yields the database collation; "C" keeps the partition sort below byte-wise, like the column.
       r.stream_id COLLATE "C",
       r.expected_kind,
       r.expected_version,
       r.commit_id,
       r.event_count,
       -- Position of the request among the requests to its stream, in batch order.
       row_number() OVER (PARTITION BY r.stream_id COLLATE "C" ORDER BY r.ord),
       -- The first occurrence of a commit id in the batch wins; later ones and already committed ones are duplicates.
       row_number() OVER (PARTITION BY r.commit_id ORDER BY r.ord) > 1 OR r.commit_id = ANY (p_existing_commits),
       -- 1-based offset of the request's first event in the flattened event arrays.
       1 + coalesce(sum(r.event_count) OVER (ORDER BY r.ord ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0)
FROM unnest(p_stream_ids, p_expected_kinds, p_expected_versions, p_commit_ids, p_event_counts)
         WITH ORDINALITY AS r(stream_id, expected_kind, expected_version, commit_id, event_count, ord)
$fn$;

-- The head of every distinct stream in the batch, -1 if the stream has no events. The lateral max() lets the planner
-- use the (stream_id, stream_position) index backwards with a limit, so each probe is O(1) however long the stream
-- is. Inlined like zip_requests.
CREATE OR REPLACE FUNCTION __schema__.get_stream_heads(p_stream_ids text[])
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
FROM (SELECT DISTINCT u.stream_id COLLATE "C" AS stream_id
      FROM unnest(p_stream_ids) AS u(stream_id)) AS s
         CROSS JOIN LATERAL (SELECT max(e.stream_position) AS head
                             FROM __schema__.event_log AS e
                             WHERE e.stream_id = s.stream_id) AS h
$fn$;
