using DomainBlocks.Testing.Events;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class MongoEventStoreBuilderTests
{
    [Test]
    public void Build_WithoutConnection_ThrowsNamingTheMethodsToCall()
    {
        var ex = Should.Throw<InvalidOperationException>(() => new MongoEventStoreBuilder<object>().Build());

        ex.Message.ShouldContain("UseClient");
        ex.Message.ShouldContain("UseClientSettings");
        ex.Message.ShouldContain("UseConnectionString");
    }

    [Test]
    public async Task ConfigureOptions_IsAppliedToTheStoreOptions()
    {
        using var client = new MongoClient("mongodb://localhost");
        var seen = new List<string>();

        await using var store = new MongoEventStoreBuilder<object>()
            .UseClient(client)
            .ConfigureOptions(o => o.DatabaseName = "first")
            .ConfigureOptions(o => seen.Add(o.DatabaseName))
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();

        seen.ShouldBe(["first"]);
    }
}