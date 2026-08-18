namespace DomainBlocks.EventStore.Abstractions.New;

internal sealed class EventReadBuilder<TEvent, TPos>(
    Func<ReadDefinition<TPos>, IAsyncEnumerable<TEvent>> reader) :
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
        _definition = ReadDefinition.ForwardFromStart<TPos>();
        return this;
    }

    IEventReadBuilder<TEvent, TPos>.IRead IEventReadBuilder<TEvent, TPos>.From(TPos position)
    {
        _definition = ReadDefinition.ForwardFrom(position);
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
        _definition = ReadDefinition.BackwardFromEnd<TPos>();
        return this;
    }

    IEventReadBuilder<TEvent, TPos>.IRead IEventReadBuilder<TEvent, TPos>.IBackward.From(TPos position)
    {
        _definition = ReadDefinition.BackwardFrom(position);
        return this;
    }
}