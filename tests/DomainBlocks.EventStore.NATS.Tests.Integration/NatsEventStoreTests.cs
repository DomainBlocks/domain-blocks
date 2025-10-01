using DomainBlocks.EventStore.Abstractions;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NUnit.Framework;

namespace DomainBlocks.EventStore.NATS.Tests.Integration;

public class NatsEventStoreTests
{
    [Test]
    public async Task Test()
    {
        await using var natsClient = new NatsClient();
        var js = new NatsJSContext(natsClient.Connection);

        var streamConfig = new StreamConfig
        {
            Name = "EVENTS",
            Subjects = ["events.*"],
            Storage = StreamConfigStorage.File
        };

        var stream = await js.CreateStreamAsync(streamConfig);

        var header1 = new UncommittedEventHeader("event1").WithMetadata("key1", "Magic Jon is cool");
        var event1 = new UncommittedEvent<byte[]>(header1, []);

        var header2 = new UncommittedEventHeader("event2").WithMetadata("key2", "Dan is more cool in general");
        var event2 = new UncommittedEvent<byte[]>(header2, []);

        var eventStore = new NatsEventStore();

        await eventStore.AppendToStreamAsync("stream1", [event1, event2]);

        var result = await eventStore.ReadStreamAsync("stream1");
    }
}