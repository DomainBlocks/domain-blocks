using DomainBlocks.EventStore.Internal;
using DomainBlocks.Testing.Events;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresEventStoreAppendTests
{
    [Test]
    public async Task AppendAsync_TooLongTimeout_Throws()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder("Host=localhost").Build();

        await using var store = new PostgresEventStoreBuilder<object>()
            .UseDataSource(dataSource)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        TestEvent[] events = [new() { Value = "a" }];
        var options = new AppendOptions { Timeout = CancellationTokenSource.MaxSupportedDelay + TimeSpan.FromTicks(1) };

        var ex = await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            store.AppendAsync("s1", events, options: options));

        ex.ParamName.ShouldBe("timeout");
    }
}