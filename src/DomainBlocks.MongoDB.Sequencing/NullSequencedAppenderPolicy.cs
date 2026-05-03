namespace DomainBlocks.MongoDB.Sequencing;

public sealed class NullSequencedAppenderPolicy<TDocument, TContext> : IMongoSequencedAppenderPolicy<TContext>
{
    public static readonly NullSequencedAppenderPolicy<TDocument, TContext> Instance = new();

    public ValueTask OnCommittingAsync(
        IReadOnlyList<AppendEntry<TContext>> batch,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public void OnConflict(AppendEntry<TContext> conflict)
    {
    }
}