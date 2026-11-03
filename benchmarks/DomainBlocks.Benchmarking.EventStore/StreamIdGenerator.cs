namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// Generates stream IDs for benchmark workers. Each worker appends to a stream of its own until the stream holds a set
/// number of events, and then starts a new stream. The first append to each stream takes the store's new-stream path.
/// Stream IDs are unique across runs.
/// </summary>
/// <remarks>
/// A worker's state is used only by that worker. That needs no synchronization, because a benchmark worker has one
/// operation in flight at a time.
/// </remarks>
public sealed class StreamIdGenerator
{
    private readonly string _runId = $"bench-{Guid.NewGuid():N}";
    private readonly int _eventsPerStream;
    private readonly WorkerStream[] _workerStreams;

    /// <param name="workerCount">The number of workers, which are numbered from zero.</param>
    /// <param name="eventsPerStream">
    /// The number of events each stream holds before its worker starts a new stream.
    /// </param>
    public StreamIdGenerator(int workerCount, int eventsPerStream)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventsPerStream);

        _eventsPerStream = eventsPerStream;
        _workerStreams = new WorkerStream[workerCount];
    }

    /// <summary>
    /// Returns the ID of the stream that a worker's next append of one event goes to.
    /// </summary>
    public string Next(int workerIndex)
    {
        ref var stream = ref _workerStreams[workerIndex];

        if (stream.Id is null || stream.EventCount == _eventsPerStream)
        {
            // The stream number comes before the worker index so that streams created at about the same time share a
            // prefix and sit next to each other in the stream ID index, as time-ordered IDs such as GUID v7 do. With
            // the worker index first, new streams would be inserted in as many places as there are workers, so how
            // scattered the inserts are would change with the in-flight count. That cost about 15% of append
            // throughput in both stores at 1,000 in flight.
            stream.Id = $"{_runId}-{stream.Number++}-{workerIndex}";
            stream.EventCount = 0;
        }

        stream.EventCount++;
        return stream.Id;
    }

    private struct WorkerStream
    {
        public string? Id;
        public int Number;
        public int EventCount;
    }
}