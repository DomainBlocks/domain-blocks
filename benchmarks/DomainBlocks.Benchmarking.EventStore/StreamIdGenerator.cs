namespace DomainBlocks.Benchmarking.EventStore;

/// <summary>
/// Generates stream IDs for benchmark workers. Each worker appends to a stream of its own until the stream holds a set
/// number of events, and then starts a new stream. The first append to each stream takes the store's new-stream path.
/// Stream IDs are unique across runs. If a name count is given, each new stream also gets one of that many names,
/// drawn at random, like streams named after the features that create them.
/// </summary>
/// <remarks>
/// A worker's state is used only by that worker. That needs no synchronization, because a benchmark worker has one
/// operation in flight at a time.
/// </remarks>
public sealed class StreamIdGenerator
{
    private readonly string _runId = $"bench-{Guid.NewGuid():N}";
    private readonly int _eventsPerStream;
    private readonly int _nameCount;
    private readonly WorkerStream[] _workerStreams;

    /// <param name="workerCount">The number of workers, which are numbered from zero.</param>
    /// <param name="eventsPerStream">
    /// The number of events each stream holds before its worker starts a new stream.
    /// </param>
    /// <param name="nameCount">
    /// The number of names that new streams draw from, or <see langword="null"/> for streams with no name.
    /// </param>
    public StreamIdGenerator(int workerCount, int eventsPerStream, int? nameCount = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventsPerStream);

        if (nameCount is { } count)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count, nameof(nameCount));

        _eventsPerStream = eventsPerStream;
        _nameCount = nameCount ?? 0;
        _workerStreams = new WorkerStream[workerCount];

        if (nameCount is not null)
        {
            // Adjacent seeds, such as worker indexes, give correlated draws. With 1,000 workers and 1,000 names, the
            // workers' current streams had anywhere from 100 to 968 distinct names, where random draws give about 632.
            // So each worker's seed comes from one generator with a fixed seed, which also gives each worker the same
            // names in every run.
            var seeds = new Random(0);
            for (var i = 0; i < workerCount; i++)
                _workerStreams[i].NameRandom = new Random(seeds.Next());
        }
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
            // throughput in both stores at 1,000 in flight. A name comes before the stream number, as a feature name
            // would, so that each new stream is inserted next to the other streams with the same name.
            stream.Id = stream.NameRandom is { } nameRandom
                ? $"{_runId}-n{nameRandom.Next(_nameCount)}-{stream.Number++}-{workerIndex}"
                : $"{_runId}-{stream.Number++}-{workerIndex}";
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
        public Random? NameRandom;
    }
}