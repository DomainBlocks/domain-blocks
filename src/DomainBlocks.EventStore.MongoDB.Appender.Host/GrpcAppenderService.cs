using System.Diagnostics;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Api.Appender.V0;
using Grpc.Core;
using AppendToStreamOptions = DomainBlocks.EventStore.Abstractions.AppendToStreamOptions;
using ExpectedStreamState = DomainBlocks.EventStore.Abstractions.ExpectedStreamState;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

public sealed class GrpcAppenderService(EventAppender appender) : AppenderService.AppenderServiceBase
{
    public override async Task<AppendToStreamResponse> AppendToStream(
        AppendToStreamRequest request,
        ServerCallContext context)
    {
        var events = request.Batch.Events
            .Select(x => EncodedEvent.Create(x.EventName, x.EventData.Memory, x.Metadata.Memory));

        var options = MapOptions(request.Options);

        await appender.AppendToStreamAsync(request.StreamId, events, options, context.CancellationToken);

        var response = new AppendToStreamResponse();

        return response;
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