namespace DomainBlocks.Testing.Integration.Benchmarking;

/// <summary>
/// One append (or append-shaped) operation against the system under test. <paramref name="workerIndex"/> lets callers
/// spread workers over several store instances without synchronisation; <paramref name="streamId"/> is unique per call.
/// </summary>
public delegate Task BenchmarkOperation(int workerIndex, string streamId, CancellationToken cancellationToken);