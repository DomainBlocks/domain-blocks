namespace DomainBlocks.Infrastructure.MongoDB.ChangeStreams;

public interface IChangeStreamSubject<out TDocument>
{
    IDisposable Attach(IChangeStreamObserver<TDocument> observer);

    IDisposable AttachGroup(IEnumerable<IChangeStreamObserver<TDocument>> observers);

    IChangeStreamConnection Connect();
}