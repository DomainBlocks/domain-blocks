using DomainBlocks.EventStore.Abstractions;
using NATS.Client.Core;
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
        var jsContext = natsClient.CreateJetStreamContext();

        try
        {
            await jsContext.DeleteStreamAsync("EVENTS");
        }
        catch (NatsJSApiException ex) when (ex.Message == "stream not found")
        {
            // ignore
        }

        var streamConfig = new StreamConfig
        {
            Name = "EVENTS",
            Subjects = ["events.*"],
            Storage = StreamConfigStorage.File,
            AllowAtomicPublish = true
        };

        await jsContext.CreateStreamAsync(streamConfig);

        var events = Enumerable.Range(1, 100).Select(i => CreateEvent($"event{i}"));
        var eventStore = new NatsEventStore(natsClient);
        await eventStore.AppendToStreamAsync("stream1", events);

        //var result = await eventStore.ReadStreamAsync("stream1");
    }

    [Test]
    public async Task AtomicPublishTest()
    {
        await using var natsClient = new NatsClient();
        var js = new NatsJSContext(natsClient.Connection);

        try
        {
            await js.DeleteStreamAsync("EVENTS");
        }
        catch (NatsJSApiException ex) when (ex.Message == "stream not found")
        {
            // ignore
        }

        var streamConfig = new StreamConfig
        {
            Name = "EVENTS",
            Subjects = ["events.*"],
            Storage = StreamConfigStorage.File,
            AllowAtomicPublish = true
        };

        await js.CreateStreamAsync(streamConfig);

        var natsHeaders = new NatsHeaders
        {
            { "Nats-Batch-Id", "uuid" },
            { "Nats-Batch-Sequence", "1" },
        };

        var ack = await js.TryPublishAsync("events.test", new byte[] { 1 }, headers: natsHeaders);
    }

    private static UncommittedEvent<byte[]> CreateEvent(string name)
    {
        return new UncommittedEvent<byte[]>(new UncommittedEventHeader(name), []);
    }
}