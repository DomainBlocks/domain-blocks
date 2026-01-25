using System.Diagnostics;
using System.Runtime.InteropServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Api.Appender.V0;
using Google.Protobuf.Collections;
using Grpc.Core;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using AppendToStreamOptions = DomainBlocks.EventStore.Abstractions.AppendToStreamOptions;
using ExpectedStreamState = DomainBlocks.EventStore.Abstractions.ExpectedStreamState;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

public sealed class GrpcAppenderService(EventAppender appender) : AppenderService.AppenderServiceBase
{
    public override async Task<AppendToStreamResponse> AppendToStream(
        AppendToStreamRequest request,
        ServerCallContext context)
    {
        var events = ToEventDocuments(request.Batch.Events);
        var options = MapOptions(request.Options);

        await appender.AppendToStreamAsync(request.StreamId, events, options, context.CancellationToken);

        var response = new AppendToStreamResponse();

        return response;
    }

    private static List<Schema.EventDocument> ToEventDocuments(RepeatedField<Api.Appender.V0.AppendEvent> events)
    {
        var documents = new List<Schema.EventDocument>(events.Count);

        foreach (var e in events)
        {
            documents.Add(new Schema.EventDocument
            {
                EventName = e.EventName,
                EventData = ToRawBsonDocument(e.EventData.Memory),
                Metadata = e.Metadata.IsEmpty ? BsonNull.Value : ToRawBsonDocument(e.Metadata.Memory)
            });
        }

        return documents;
    }

    private static RawBsonDocument ToRawBsonDocument(ReadOnlyMemory<byte> memory)
    {
        if (!MemoryMarshal.TryGetArray(memory, out var segment) || segment.Array is null)
            return new RawBsonDocument(memory.ToArray());

        // Profile this
        switch (segment.Offset)
        {
            case 0 when segment.Count == segment.Array.Length:
                // Full array: simplest path (driver wraps internally)
                return new RawBsonDocument(segment.Array);
            case 0:
                // Prefix of array: avoid ByteBufferSlice by using length-limited buffer
                return new RawBsonDocument(new ByteArrayBuffer(segment.Array, segment.Count, isReadOnly: true));
            default:
                // True slice: need buffer + slice
                var buffer = new ByteArrayBuffer(segment.Array, isReadOnly: true);
                var slice = new ByteBufferSlice(buffer, segment.Offset, segment.Count);
                return new RawBsonDocument(slice);
        }
    }

    private static AppendToStreamOptions MapOptions(Api.Appender.V0.AppendToStreamOptions grpcOptions)
    {
        var expectedState = grpcOptions.ExpectedState.Kind switch
        {
            Api.Appender.V0.ExpectedStreamStateKind.Unspecified => ExpectedStreamState.Any,
            Api.Appender.V0.ExpectedStreamStateKind.Any => ExpectedStreamState.Any,
            Api.Appender.V0.ExpectedStreamStateKind.StreamDoesNotExist => ExpectedStreamState.StreamDoesNotExist,
            Api.Appender.V0.ExpectedStreamStateKind.StreamExists => ExpectedStreamState.StreamExists,
            Api.Appender.V0.ExpectedStreamStateKind.SpecificVersion => ToSpecificVersion(grpcOptions.ExpectedState),
            _ => throw new UnreachableException(
                $"Unexpected ExpectedStreamStateKind '{grpcOptions.ExpectedState.Kind}'.")
        };

        return new AppendToStreamOptions
        {
            ExpectedState = expectedState
        };

        static ExpectedStreamState ToSpecificVersion(Api.Appender.V0.ExpectedStreamState grpcExpectedState)
        {
            if (!grpcExpectedState.HasVersion)
            {
                throw new RpcException(new Status(
                    StatusCode.InvalidArgument,
                    "ExpectedStreamState.version must be set when kind is SPECIFIC_VERSION."));
            }

            var version = new StreamVersion(grpcExpectedState.Version);

            return ExpectedStreamState.SpecificVersion(version);
        }
    }
}