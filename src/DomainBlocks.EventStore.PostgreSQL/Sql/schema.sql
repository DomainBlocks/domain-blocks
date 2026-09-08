-- Event store schema. The __schema__ token is replaced with the validated schema name before execution. The script is
-- executed inside a single transaction; the advisory lock serializes concurrent initializers so that racing
-- CREATE ... IF NOT EXISTS statements cannot fail on catalog uniqueness.

SELECT pg_advisory_xact_lock(hashtext('__schema__:init'));

CREATE SCHEMA IF NOT EXISTS __schema__;

CREATE TABLE IF NOT EXISTS __schema__.event_log (
    position         bigint      NOT NULL,
    stream_id        text        NOT NULL,
    stream_position  bigint      NOT NULL,
    commit_id        uuid        NOT NULL,
    commit_index     integer     NOT NULL,
    event_name       text        NOT NULL,
    event_data       jsonb,
    event_data_bytes bytea,
    metadata         jsonb,
    created_at       timestamptz NOT NULL,
    CONSTRAINT event_log_pkey PRIMARY KEY (position),
    CONSTRAINT event_log_stream_id_stream_position_key UNIQUE (stream_id, stream_position),
    CONSTRAINT event_log_event_data_check CHECK ((event_data IS NULL) <> (event_data_bytes IS NULL)),
    CONSTRAINT event_log_stream_id_check CHECK (stream_id <> ''),
    CONSTRAINT event_log_stream_position_check CHECK (stream_position >= 0),
    CONSTRAINT event_log_commit_index_check CHECK (commit_index >= 0)
);

CREATE INDEX IF NOT EXISTS event_log_commit_id_idx ON __schema__.event_log (commit_id);

-- A single hot row per sequence. The low fill factor leaves room for HOT updates so the row never leaves its page.
CREATE TABLE IF NOT EXISTS __schema__.sequences (
    name text   NOT NULL,
    next bigint NOT NULL,
    CONSTRAINT sequences_pkey PRIMARY KEY (name),
    CONSTRAINT sequences_next_check CHECK (next >= 0)
) WITH (fillfactor = 50);

INSERT INTO __schema__.sequences (name, next)
VALUES ('event_log', 0)
ON CONFLICT (name) DO NOTHING;
