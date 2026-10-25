using DomainBlocks.Testing.Events;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresEventStoreBuilderTests
{
    private static readonly TimeSpan[] NonPositiveIntervals =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(-1),
        Timeout.InfiniteTimeSpan
    ];

    [Test]
    public void Build_WithoutConnection_ThrowsNamingTheMethodsToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() => new PostgresEventStoreBuilder<object>().Build());

        ex.Message.ShouldContain("UseDataSource");
        ex.Message.ShouldContain("UseConnectionString");
    }

    [Test]
    public async Task ConfigureOptions_IsAppliedToTheStoreOptions()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();
        var seen = new List<string>();

        await using var store = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .ConfigureOptions(o => o.Schema = "first")
            .ConfigureOptions(o => seen.Add(o.Schema))
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        seen.ShouldBe(["first"]);
    }

    [TestCaseSource(nameof(NonPositiveIntervals))]
    public async Task Build_NonPositiveInactiveSlotTimeout_Throws(TimeSpan timeout)
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();

        var builder = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .ConfigureOptions(o => o.Replication.InactiveSlotTimeout = timeout)
            .ConfigureCodec(x => x.MapEvent<TestEvent>());

        Should.Throw<ArgumentOutOfRangeException>(builder.Build);
    }
}