namespace DomainBlocks.V1.Abstractions;

public sealed class ReadStreamResult
{
    private ReadStreamResult(ReadStreamStatus status, IAsyncEnumerable<StoredEventRecord> eventRecords)
    {
        Status = status;
        EventRecords = eventRecords;
    }
    
    public static ReadStreamResult NotFound() => new(ReadStreamStatus.StreamNotFound, CreateEmptyEventRecords());
    
    public static ReadStreamResult Success(IAsyncEnumerable<StoredEventRecord> eventRecords) =>
        new(ReadStreamStatus.Success, eventRecords);
    
    public ReadStreamStatus Status { get; } 
    public IAsyncEnumerable<StoredEventRecord> EventRecords { get; }
    
    // To avoid taking a dependency on System.Linq.Async, we provide an implementation of an empty IAsyncEnumerable.
    // If we find that we need other things from that assembly in the future, we may revisit this decision.
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    private static async IAsyncEnumerable<StoredEventRecord> CreateEmptyEventRecords()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        yield break;
    }
}