namespace DomainBlocks.Benchmarking;

/// <summary>
/// One operation against the system under test. <paramref name="workerIndex"/> lets callers spread workers over several
/// instances, or keep state for each worker, without synchronization. A worker has one operation in flight at a time.
/// </summary>
public delegate Task BenchmarkOperation(int workerIndex, CancellationToken cancellationToken);