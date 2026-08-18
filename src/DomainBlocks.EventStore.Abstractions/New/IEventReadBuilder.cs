namespace DomainBlocks.EventStore.Abstractions.New;

public interface IEventReadBuilder<out TEvent, in TPos> where TEvent : notnull where TPos : notnull
{
    IRead FromStart();
    IRead From(TPos position);
    IRead After(TPos position);
    IBackward Backward();

    interface IRead
    {
        IAsyncEnumerable<TEvent> ToAsyncEnumerable();
    }

    interface IBackward
    {
        IRead FromEnd();
        IRead From(TPos position);
    }
}