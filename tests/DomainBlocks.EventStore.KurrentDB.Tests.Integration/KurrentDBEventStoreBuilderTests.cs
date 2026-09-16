using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore.KurrentDB;
using KurrentDB.Client;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

/// <summary>
/// The builder's two connection paths against a real server, and the JSON defaults.
/// </summary>
public class KurrentDBEventStoreBuilderTests
{
    [Test]
    public async Task OwnedPath_AppendReadDispose_Works()
    {
        var store = new KurrentDBEventStoreBuilder<object>()
            .UseConnectionString(KurrentDBTestEnvironment.ConnectionString)
            .MapEvent<TestEvent>()
            .Build();

        var streamId = $"bld-{Guid.NewGuid():N}";
        var appended = new TestEvent { Value = "owned" };

        await using (store)
        {
            await store.AppendAsync(streamId, [AppendableEvent.Create<object>(appended, [new("k", "v")])]);

            var read = (await store.ReadStream(streamId).ToArrayAsync()).ShouldHaveSingleItem();
            read.Payload.ShouldBe(appended);
            read.Context.Metadata.ShouldBe(new Dictionary<string, string> { ["k"] = "v" });
        }
    }

    [Test]
    public async Task BorrowedPath_DisposingTheStore_LeavesTheClientUsable()
    {
        await using var client = new KurrentDBClient(KurrentDBClientSettings.Create(KurrentDBTestEnvironment.ConnectionString));

        var store = new KurrentDBEventStoreBuilder<object>()
            .UseClient(client)
            .MapEvent<TestEvent>()
            .Build();

        var streamId = $"bld-{Guid.NewGuid():N}";
        await store.AppendAsync(streamId, [AppendableEvent.Create<object>(new TestEvent { Value = "v" })]);
        await store.DisposeAsync();

        var result = client.ReadStreamAsync(Direction.Forwards, streamId, global::KurrentDB.Client.StreamPosition.Start);
        (await result.ReadState).ShouldBe(ReadState.Ok);
    }
}