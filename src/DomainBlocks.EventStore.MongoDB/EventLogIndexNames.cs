namespace DomainBlocks.EventStore.MongoDB;

internal static class EventLogIndexNames
{
    public const string UniqueStreamVersion = "event_log_stream_id_stream_version_ux";
    public const string CommitId = "event_log_commit_id_ix";
}