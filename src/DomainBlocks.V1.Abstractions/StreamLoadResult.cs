namespace DomainBlocks.V1.Abstractions;

public sealed class StreamLoadResult
{
    private StreamLoadResult(StreamLoadStatus loadStatus, IAsyncEnumerable<StoredEventRecord> eventRecords)
    {
        LoadStatus = loadStatus;
        EventRecords = eventRecords;
    }
    
    public static StreamLoadResult NotFound() => new(StreamLoadStatus.StreamNotFound,
        AsyncEnumerable.Empty<StoredEventRecord>());
    
    public static StreamLoadResult Success(IAsyncEnumerable<StoredEventRecord> eventRecords) =>
        new(StreamLoadStatus.Success, eventRecords);
    
    public StreamLoadStatus LoadStatus { get; } 
    public IAsyncEnumerable<StoredEventRecord> EventRecords { get; }
}