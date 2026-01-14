using DomainBlocks.EventStore.Api.Appender.V0;
using Grpc.Core;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

public sealed class AppenderServiceImpl : AppenderService.AppenderServiceBase
{
    public override Task<AppendToStreamResponse> AppendToStream(
        AppendToStreamRequest request,
        ServerCallContext context)
    {
        var response = new AppendToStreamResponse();
        return Task.FromResult(response);
    }
}