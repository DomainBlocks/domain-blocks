namespace DomainBlocks.Projections.Sql
{
    public record SqlContextSettings
    {
        public static SqlContextSettings Default { get; } = new(true, true);

        public SqlContextSettings(bool useTransactionBeforeCaughtUp, bool handleLiveEventsInTransaction)
        {
            UseTransactionBeforeCaughtUp = useTransactionBeforeCaughtUp;
            HandleLiveEventsInTransaction = handleLiveEventsInTransaction;
        }

        public bool UseTransactionBeforeCaughtUp { get; init; }
        public bool HandleLiveEventsInTransaction { get; init; }
    }
}