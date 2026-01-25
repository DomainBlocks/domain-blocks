using DomainBlocks.EventStore.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

internal sealed class ExceptionInterceptor : Interceptor
{
    private static readonly RpcStatusMapper[] StatusMappers =
    [
        MapStreamAppendConflict
    ];

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex)
        {
            foreach (var mapper in StatusMappers)
            {
                var status = mapper(ex);
                if (status is not null)
                    throw status.ToRpcException();
            }

            throw;
        }
    }

    private static Google.Rpc.Status? MapStreamAppendConflict(Exception exception)
    {
        if (exception is not StreamAppendConflictException ex)
            return null;

        return new Google.Rpc.Status
        {
            Code = (int)Code.FailedPrecondition,
            Message = ex.Message,
            Details =
            {
                Any.Pack(new ErrorInfo
                {
                    Reason = "STREAM_APPEND_CONFLICT",
                    Domain = "domainblocks",
                    Metadata =
                    {
                        { "streamId", ex.StreamId },
                        { "expectedState", ex.ExpectedState.ToString() },
                        { "actualState", ex.ActualState?.ToString() ?? "unavailable" }
                    }
                })
            }
        };
    }

    private delegate Google.Rpc.Status? RpcStatusMapper(Exception exception);
}