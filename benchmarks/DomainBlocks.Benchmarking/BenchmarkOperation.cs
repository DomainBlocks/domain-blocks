namespace DomainBlocks.Benchmarking;

/// <summary>
/// One operation against the system under test. <paramref name="index"/> lets callers spread operations over several
/// instances, or keep state for each worker, without synchronization. In a closed loop, it's the worker's index, and a
/// worker has one operation in flight at a time. In an open loop, it's the arrival's sequence number, and operations
/// overlap.
/// </summary>
public delegate Task BenchmarkOperation(int index, CancellationToken cancellationToken);