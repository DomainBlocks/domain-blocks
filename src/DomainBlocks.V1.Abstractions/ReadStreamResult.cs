namespace DomainBlocks.V1.Abstractions;

public sealed class ReadStreamResult
{
    private ReadStreamResult(ReadStreamStatus status, IAsyncEnumerable<StoredEventRecord> eventRecords)
    {
        Status = status;
        EventRecords = eventRecords;
    }
    
    public static ReadStreamResult NotFound() => new(ReadStreamStatus.StreamNotFound, 
        AsyncEnumerable.Empty<StoredEventRecord>());
    
    public static ReadStreamResult Success(IAsyncEnumerable<StoredEventRecord> eventRecords) =>
        new(ReadStreamStatus.Success, eventRecords);
    
    public ReadStreamStatus Status { get; } 
    public IAsyncEnumerable<StoredEventRecord> EventRecords { get; }
}