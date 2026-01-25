using DomainBlocks.EventStore.Abstractions;
using Google.Rpc;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class ExceptionInterceptor : Interceptor
{
    private static readonly RpcErrorMapper[] ErrorMappers =
    [
        MapStreamAppendConflict
    ];

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var call = continuation(request, context);

        return new AsyncUnaryCall<TResponse>(
            HandleResponse(call.ResponseAsync),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private static async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> inner)
    {
        try
        {
            return await inner.ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            var status = ex.GetRpcStatus();
            if (status is null)
                throw;

            var mappedException = MapException(status, ex);
            if (mappedException is not null)
                throw mappedException;

            throw;
        }
    }

    private static Exception? MapException(Google.Rpc.Status status, RpcException ex)
    {
        return status.Details
            .Where(x => x.Is(ErrorInfo.Descriptor))
            .SelectMany(x =>
            {
                var errorInfo = x.Unpack<ErrorInfo>();
                return ErrorMappers.Select(mapper => mapper(errorInfo, ex));
            })
            .OfType<Exception>()
            .FirstOrDefault();
    }

    private static StreamAppendConflictException? MapStreamAppendConflict(
        ErrorInfo errorInfo,
        RpcException rpcException)
    {
        var metadata = errorInfo.Metadata;

        if (errorInfo.Reason != "STREAM_APPEND_CONFLICT" || errorInfo.Domain != "domainblocks")
            return null;

        var streamId = metadata["streamId"];
        if (streamId == null)
            return null;

        var expectedStateText = metadata["expectedState"];
        if (expectedStateText == null)
            return null;

        if (!ExpectedStreamState.TryParse(expectedStateText, out var expectedState))
            return null;

        _ = StreamState.TryParse(metadata["actualState"], out var actualState);

        return new StreamAppendConflictException(streamId, expectedState.Value, actualState, rpcException);
    }

    private delegate Exception? RpcErrorMapper(ErrorInfo errorInfo, RpcException rpcException);
}