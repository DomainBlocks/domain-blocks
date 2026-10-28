using DomainBlocks.EventStore.Internal;
using DomainBlocks.Testing.Events;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class MongoEventStoreAppendTests
{
    [Test]
    public async Task AppendAsync_TooLongTimeout_Throws()
    {
        using var client = new MongoClient("mongodb://localhost");

        await using var store = new MongoEventStoreBuilder<object>()
            .UseClient(client)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        TestEvent[] events = [new() { Value = "a" }];
        var options = new AppendOptions { Timeout = CancellationTokenSource.MaxSupportedDelay + TimeSpan.FromTicks(1) };

        var ex = await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            store.AppendAsync("s1", events, options: options));

        ex.ParamName.ShouldBe("options.Timeout");
    }
}