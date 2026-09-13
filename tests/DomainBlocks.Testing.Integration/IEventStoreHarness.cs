using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;

namespace DomainBlocks.Testing.Integration;

/// <summary>
/// Everything a shared suite needs from one backend: the lifetime of a store's schema or database and how to build a
/// store over it. A concrete fixture binds a suite to a backend by passing its harness, so the suites stay
/// backend-agnostic and the fixtures stay one line.
/// </summary>
public interface IEventStoreHarness<TStreamPos, TLogPos>
    where TStreamPos : notnull
    where TLogPos : notnull
{
    /// <summary>
    /// The event formats the backend's codec can be created with, for suites that cover each of them.
    /// </summary>
    IReadOnlyList<EventFormat> SupportedFormats { get; }

    /// <summary>
    /// Creates the schema or database for a fixture. <paramref name="name"/> is unique per fixture.
    /// </summary>
    Task InitializeAsync(string name);

    /// <summary>
    /// Empties the event log, keeping the schema or database, for suites whose tests assume an empty log.
    /// </summary>
    Task ResetAsync();

    Task DropAsync();

    IEventStore<object, string, TStreamPos, TLogPos> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "");

    TStreamPos CreateStreamPosition(ulong value);

    TLogPos CreateLogPosition(ulong value);

    /// <summary>
    /// Describes the store and the settings that affect a benchmark's result, for the report header. Null when there
    /// is nothing to say.
    /// </summary>
    Task<string?> DescribeAsync();
}