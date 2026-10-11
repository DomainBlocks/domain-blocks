namespace DomainBlocks.EventStore.MongoDB;

internal static class SubscriptionPositionAdapter
{
    public static readonly SubscriptionPositionAdapter<LogPosition> All = new()
    {
        FieldName = EventLogEntry.FieldNames.Position,
        Factory = static pos => LogPosition.FromInt64(pos),
        Selector = static ctx => ctx.LogPosition
    };

    public static readonly SubscriptionPositionAdapter<StreamPosition> Stream = new()
    {
        FieldName = EventLogEntry.FieldNames.StreamPosition,
        Factory = static pos => StreamPosition.FromInt64(pos),
        Selector = static ctx => ctx.StreamPosition
    };
}

internal sealed class SubscriptionPositionAdapter<TPos> where TPos : struct, IPosition<TPos>
{
    public required string FieldName { get; init; }
    public required Func<long, TPos> Factory { get; init; }
    public required Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> Selector { get; init; }
}