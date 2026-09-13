using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.ContractMapping;
using DomainBlocks.EventStore.TypeMapping;
using NUnit.Framework;

namespace DomainBlocks.Testing.Integration;

/// <summary>
/// Base of every shared suite. Owns the store's lifecycle through a backend harness, so that a concrete fixture only
/// has to say which harness it uses.
/// </summary>
public abstract class EventStoreTestBase<TStreamPos, TLogPos>(IEventStoreHarness<TStreamPos, TLogPos> harness)
    where TStreamPos : notnull
    where TLogPos : notnull
{
    protected IEventStoreHarness<TStreamPos, TLogPos> Harness { get; } = harness;

    /// <summary>
    /// Whether the event log is emptied before each test. Suites whose tests assume an empty log override this; the
    /// rest isolate their tests with unique stream ids instead.
    /// </summary>
    protected virtual bool ResetLogBeforeEachTest => false;

    [OneTimeSetUp]
    public Task InitializeStoreAsync() => Harness.InitializeAsync(TestStoreName.For(this));

    [OneTimeTearDown]
    public Task DropStoreAsync() => Harness.DropAsync();

    [SetUp]
    public async Task ResetLogIfRequiredAsync()
    {
        if (ResetLogBeforeEachTest)
            await Harness.ResetAsync();
    }

    protected IEventStore<object, string, TStreamPos, TLogPos> CreateEventStore(
        EventTypeMap eventTypeMap,
        EventFormat? eventFormat = null,
        IEnumerable<IEventContractMapper<object>>? contractMappers = null,
        string loggerNameSuffix = "")
    {
        return Harness.CreateEventStore(eventTypeMap, eventFormat, contractMappers, loggerNameSuffix);
    }

    protected TStreamPos CreateStreamPosition(ulong value) => Harness.CreateStreamPosition(value);

    protected TLogPos CreateLogPosition(ulong value) => Harness.CreateLogPosition(value);
}