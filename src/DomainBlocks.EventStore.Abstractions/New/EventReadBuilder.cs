namespace DomainBlocks.EventStore.Abstractions.New;

internal sealed class EventReadBuilder<TEvent, TPos>(Func<ReadDefinition<TPos>, IAsyncEnumerable<TEvent>> reader) :
    IEventReadBuilder<TEvent, TPos>,
    IEventReadBuilder<TEvent, TPos>.IRead,
    IEventReadBuilder<TEvent, TPos>.IBackward
    where TEvent : notnull
    where TPos : notnull
{
    private ReadDefinition<TPos>? _definition;

    // ─────────────────────────────────────────────────────────────
    // IEventReadBuilder<TEvent, TPos>
    // ─────────────────────────────────────────────────────────────

    IEventReadBuilder<TEvent, TPos>.IRead IEventReadBuilder<TEvent, TPos>.FromStart()
    {
        _definition = new ReadDefinition<TPos>.Forward(ReadOrigin.Start<TPos>());
        return this;
    }

    IEventReadBuilder<TEvent, TPos>.IRead IEventReadBuilder<TEvent, TPos>.From(TPos position)
    {
        _definition = new ReadDefinition<TPos>.Forward(ReadOrigin.From(position));
        return this;
    }

    IEventReadBuilder<TEvent, TPos>.IRead IEventReadBuilder<TEvent, TPos>.After(TPos position)
    {
        _definition = new ReadDefinition<TPos>.Forward(ReadOrigin.After(position));
        return this;
    }

    IEventReadBuilder<TEvent, TPos>.IBackward IEventReadBuilder<TEvent, TPos>.Backward() => this;

    // ─────────────────────────────────────────────────────────────
    // IEventReadBuilder<TEvent, TPos>.IRead
    // ─────────────────────────────────────────────────────────────

    IAsyncEnumerable<TEvent> IEventReadBuilder<TEvent, TPos>.IRead.ToAsyncEnumerable() => reader(_definition!);

    // ─────────────────────────────────────────────────────────────
    // IEventReadBuilder<TEvent, TPos>.IBackward
    // ─────────────────────────────────────────────────────────────

    IEventReadBuilder<TEvent, TPos>.IRead IEventReadBuilder<TEvent, TPos>.IBackward.FromEnd()
    {
        _definition = new ReadDefinition<TPos>.Backward(ReadOrigin.End<TPos>());
        return this;
    }

    IEventReadBuilder<TEvent, TPos>.IRead IEventReadBuilder<TEvent, TPos>.IBackward.From(TPos position)
    {
        _definition = new ReadDefinition<TPos>.Backward(ReadOrigin.From(position));
        return this;
    }
}