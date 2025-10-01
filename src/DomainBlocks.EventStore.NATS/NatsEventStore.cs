using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.NATS.Proto;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;

namespace DomainBlocks.EventStore.NATS;

public class NatsEventStore : IEventStoreBackend<byte[]>
{
    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<byte[]>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        await using var natsClient = new NatsClient();
        var js = new NatsJSContext(natsClient.Connection);
        var subject = $"events.{streamId}";

        // Name must be provided if using FilterSubject: https://github.com/nats-io/nats.net/discussions/234
        var consumerConfig = new ConsumerConfig
        {
            DeliverPolicy = ConsumerConfigDeliverPolicy.Last,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            AckWait = TimeSpan.FromSeconds(5),
            FilterSubjects = [subject],
            //HeadersOnly = true
        };

        var consumer = await js.CreateOrUpdateConsumerAsync("EVENTS", consumerConfig, cancellationToken);

        var messages = consumer.FetchNoWaitAsync<byte[]>(
            new NatsJSFetchOpts { MaxMsgs = 1 },
            cancellationToken: cancellationToken);

        long expectedLastSubjectSeq = -1;
        long headStreamVersion = -1;

        await foreach (var msg in messages)
        {
            expectedLastSubjectSeq = long.Parse(msg.Headers!["Nats-Expected-Last-Subject-Sequence"]!);
            headStreamVersion = long.Parse(msg.Headers!["DomainBlocks-Head-Stream-Version"]!);
            var record = CommitRecord.Parser.ParseFrom(msg.Data);
            headStreamVersion = record.HeadStreamVersion;
            await msg.AckAsync(cancellationToken: cancellationToken);
        }

        // var streamInfoRequest = new StreamInfoRequest { SubjectsFilter = subject };
        // var stream = await js.GetStreamAsync("EVENTS", streamInfoRequest, cancellationToken);
        // var subjectSeq = stream.Info.State.Subjects?.TryGetValue(subject, out var seq) is true ? seq : 0;

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

        var ack = await js.PublishAsync(
            subject,
            payload,
            opts: options,
            headers: natsHeaders,
            cancellationToken: cancellationToken);

        ack.EnsureSuccess();
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