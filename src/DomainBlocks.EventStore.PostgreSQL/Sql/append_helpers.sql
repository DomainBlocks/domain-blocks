-- Helpers of append_events. The __schema__ token is replaced with the validated schema name. Each helper is either
-- inlined by the planner into the statement that calls it, or called once per batch, so the split costs nothing at
-- run time. The codes the helpers interpret are listed in append_events.sql.

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
