using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.NATS.Proto;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DomainBlocks.EventStore.NATS;

public class NatsEventStore(INatsClient natsClient) : IEventStoreBackend<byte[]>
{
    private readonly INatsJSContext _jsContext = natsClient.CreateJetStreamContext();

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<byte[]>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        var subject = $"events.{streamId}";

        // Name must be provided if using FilterSubject: https://github.com/nats-io/nats.net/discussions/234
        var consumerConfig = new ConsumerConfig
        {
            DeliverPolicy = ConsumerConfigDeliverPolicy.Last,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(5),
            FilterSubjects = [subject],
            HeadersOnly = true
        };

        var consumer = await _jsContext.CreateOrUpdateConsumerAsync("EVENTS", consumerConfig, cancellationToken);

        var messages = consumer.FetchNoWaitAsync<byte[]>(
            new NatsJSFetchOpts { MaxMsgs = 1 },
            cancellationToken: cancellationToken);

        long expectedLastSubjectSeq = -1;
        long headStreamVersion = -1;

        await foreach (var msg in messages)
        {
            expectedLastSubjectSeq = long.Parse(msg.Headers!["Nats-Expected-Last-Subject-Sequence"]!);
            headStreamVersion = long.Parse(msg.Headers!["DomainBlocks-Head-Stream-Version"]!);
            await msg.AckAsync(cancellationToken: cancellationToken);
            break;
        }

        var commitRecord = new CommitRecord
        {
            StreamId = streamId,
            HeadStreamVersion = headStreamVersion,
            CommittedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        };

        foreach (var e in events)
        {
            var eventRecord = new EventRecord
            {
                EventName = e.Header.EventName,
                Payload = ByteString.CopyFrom(e.Payload)
            };

            foreach (var (key, value) in e.Header.Metadata)
                eventRecord.Metadata.Add(key, value);

            commitRecord.EventRecords.Add(eventRecord);
            commitRecord.HeadStreamVersion += 1;
        }

        var payload = commitRecord.ToByteArray();

        var options = new NatsJSPubOpts
        {
            ExpectedLastSubjectSequence = Convert.ToUInt64(expectedLastSubjectSeq + 1)
        };

        var natsHeaders = new NatsHeaders
        {
            { "DomainBlocks-Head-Stream-Version", commitRecord.HeadStreamVersion.ToString() }
        };

        var ack = await _jsContext.PublishAsync(
            subject,
            payload,
            opts: options,
            headers: natsHeaders,
            cancellationToken: cancellationToken);

        ack.EnsureSuccess();
    }

    public async Task AppendToStreamWithAtomicBatchAsync(
        string streamId,
        IEnumerable<UncommittedEvent<byte[]>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        var subject = $"events.{streamId}";

        // Name must be provided if using FilterSubject: https://github.com/nats-io/nats.net/discussions/234
        var consumerConfig = new ConsumerConfig
        {
            DeliverPolicy = ConsumerConfigDeliverPolicy.Last,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(5),
            FilterSubjects = [subject],
            HeadersOnly = true
        };

        var consumer = await _jsContext.CreateOrUpdateConsumerAsync("EVENTS", consumerConfig, cancellationToken);

        var messages = consumer.FetchNoWaitAsync<byte[]>(
            new NatsJSFetchOpts { MaxMsgs = 1 },
            cancellationToken: cancellationToken);

        long expectedLastSubjectSeq = -1;

        await foreach (var msg in messages)
        {
            expectedLastSubjectSeq = long.Parse(msg.Headers!["Nats-Expected-Last-Subject-Sequence"]!);
            await msg.AckAsync(cancellationToken: cancellationToken);
            break;
        }

        var eventsArray = events as UncommittedEvent<byte[]>[] ?? [.. events];
        if (eventsArray.Length == 0)
            return;

        var batchId = Guid.NewGuid().ToString();
        var batchSeq = 1;

        // First message
        var firstHeaders = new NatsHeaders
        {
            { "Nats-Batch-Id", batchId },
            { "Nats-Batch-Sequence", batchSeq++.ToString() },
            { "Nats-Expected-Last-Subject-Sequence", (expectedLastSubjectSeq + 1).ToString() }
        };

        await _jsContext.Connection.PublishAsync(
            subject,
            eventsArray[0],
            firstHeaders,
            cancellationToken: cancellationToken);

        // All except first and last
        for (var i = 1; i < eventsArray.Length - 1; i++)
        {
            var e = eventsArray[i];

            var headers = new NatsHeaders
            {
                { "Nats-Batch-Id", batchId },
                { "Nats-Batch-Sequence", batchSeq++.ToString() }
            };

            await _jsContext.Connection.PublishAsync(
                subject,
                e.Payload,
                headers,
                cancellationToken: cancellationToken);
        }

        // Last message
        var lastHeaders = new NatsHeaders
        {
            { "Nats-Batch-Id", batchId },
            { "Nats-Batch-Sequence", batchSeq.ToString() },
            { "Nats-Batch-Commit", "1" }
        };

        var lastEvent = eventsArray[^1];

        var result = await _jsContext.TryPublishAsync(
            subject,
            lastEvent.Payload,
            headers: lastHeaders,
            cancellationToken: cancellationToken);

        result.Value.EnsureSuccess();
    }

    public async Task<ReadStreamResult<CommittedEvent<byte[]>>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await using var natsClient = new NatsClient();
        var js = new NatsJSContext(natsClient.Connection);
        var subject = $"events.{streamId}";

        // Name must be provided if using FilterSubject: https://github.com/nats-io/nats.net/discussions/234
        var consumerConfig = new ConsumerConfig
        {
            DeliverPolicy = ConsumerConfigDeliverPolicy.All,
            AckPolicy = ConsumerConfigAckPolicy.None,
            //AckWait = TimeSpan.FromSeconds(5),
            FilterSubjects = [subject],
            //HeadersOnly = true
        };

        var consumer = await js.CreateOrUpdateConsumerAsync("EVENTS", consumerConfig, cancellationToken);

        var messages = consumer.FetchNoWaitAsync<byte[]>(
            new NatsJSFetchOpts { MaxMsgs = 100 },
            cancellationToken: cancellationToken);

        long eventCount = 0;

        await foreach (var msg in messages)
        {
            var record = CommitRecord.Parser.ParseFrom(msg.Data);
            eventCount = record.HeadStreamVersion + 1;
        }

        return ReadStreamResult.Success<CommittedEvent<byte[]>>();
    }
}