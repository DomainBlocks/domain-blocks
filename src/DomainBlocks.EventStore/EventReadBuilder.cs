using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

internal sealed class EventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>(
    Func<ReadDefinition<TPos>, IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>> reader) :
    IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>,
    IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IRead,
    IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IBackward
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
    where TPos : notnull
{
    private ReadDefinition<TPos>? _definition;

    // ─────────────────────────────────────────────────────────────
    // IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>
    // ─────────────────────────────────────────────────────────────

    IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IRead
        IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.FromStart()
    {
        _definition = ReadDefinition.ForwardFromStart<TPos>();
        return this;
    }

    IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IRead
        IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.From(TPos position)
    {
        _definition = ReadDefinition.ForwardFrom(position);
        return this;
    }

    IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IBackward
        IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.Backward() => this;

    // ─────────────────────────────────────────────────────────────
    // IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IRead
    // ─────────────────────────────────────────────────────────────

    IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>
        IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IRead.ToAsyncEnumerable() =>
        reader(_definition!);

    // ─────────────────────────────────────────────────────────────
    // IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IBackward
    // ─────────────────────────────────────────────────────────────

    IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IRead
        IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IBackward.FromEnd()
    {
        _definition = ReadDefinition.BackwardFromEnd<TPos>();
        return this;
    }

    IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IRead
        IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, TPos>.IBackward.From(TPos position)
    {
        _definition = ReadDefinition.BackwardFrom(position);
        return this;
    }
}