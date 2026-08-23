using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

public interface IEventReadBuilder<TEvent, TStreamId, TStreamPos, TLogPos, in TPos>
    where TEvent : notnull
    where TStreamId : notnull
    where TStreamPos : notnull
    where TLogPos : notnull
    where TPos : notnull
{
    IRead FromStart();
    IRead From(TPos position);
    IBackward Backward();

    interface IRead
    {
        IAsyncEnumerable<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> ToAsyncEnumerable();
    }

    interface IBackward
    {
        IRead FromEnd();
        IRead From(TPos position);
    }
}