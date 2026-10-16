// using System.Collections.Frozen;
// using System.Threading.Channels;
// using DomainBlocks.EventStore.Filtering;
// using Microsoft.Extensions.Logging;
//
// namespace DomainBlocks.EventStore.Subscriptions;
//
// public class SubscriptionAsyncEnumerable<TEvent, TStreamId, TStreamPos, TLogPos, TLiveEvent, TPos> :
//     IAsyncEnumerable<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>>
//     where TEvent : notnull
//     where TStreamId : notnull
//     where TStreamPos : notnull
//     where TLogPos : notnull
//     where TLiveEvent : IReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>
//     where TPos : struct, IPosition<TPos>
// {
//     private readonly SubscriptionOrigin<TPos> _origin;
//     private readonly SubscriptionOptions _options;
//     private readonly ILogger? _logger;
//     private readonly string _correlationId;
//
//     public SubscriptionAsyncEnumerable(
//         SubscriptionOrigin<TPos>? origin,
//         SubscriptionOptions? options,
//         ILogger? logger)
//     {
//         _origin = origin ?? SubscriptionOrigin.End;
//         _options = options ?? SubscriptionOptions.Default;
//         _logger = logger;
//
//         _correlationId = _options.CorrelationId is { } id
//             ? CorrelationId.Reserve(id)
//             : CorrelationId.ReserveGenerated();
//     }
//
//     public IAsyncEnumerator<SubscriptionMessage<TEvent, TStreamId, TStreamPos, TLogPos>> GetAsyncEnumerator(
//         CancellationToken cancellationToken = default)
//     {
//         try
//         {
//             _logger?.SubscriptionStarted(_correlationId);
//         }
//         finally
//         {
//         }
//     }
//
//     internal sealed class LiveObserver(
//         int queueCapacity,
//         EventFilter liveFilter,
//         ILogger? logger = null,
//         string subscriptionId = "") :
//         ILiveEventObserver<TLiveEvent>,
//         IDisposable
//     {
//         private readonly Channel<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> _channel =
//             Channel.CreateBounded<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>>(
//                 new BoundedChannelOptions(queueCapacity)
//                 {
//                     SingleWriter = true,
//                     SingleReader = true
//                 });
//
//         private int _stopReason;
//         private bool _isStopped;
//
//         public ChannelReader<ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>> Reader => _channel.Reader;
//
//         public StopReason StopReason => (StopReason)Volatile.Read(ref _stopReason);
//
//         public ValueTask OnNextAsync(TLiveEvent liveEvent, CancellationToken cancellationToken)
//         {
//             if (_isStopped /*|| !liveFilter.Matches(value)*/)
//                 return ValueTask.CompletedTask;
//
//             ReadEvent<TEvent, TStreamId, TStreamPos, TLogPos> e;
//
//             try
//             {
//                 // Decodes the payload if it's lazy and copies the metadata in case it uses a shared buffer.
//                 e = ReadEvent.Create(liveEvent.Payload, liveEvent.Metadata.ToFrozenDictionary(), liveEvent.Context);
//             }
//             catch (Exception ex)
//             {
//                 _isStopped = true;
//                 _channel.Writer.TryComplete(ex);
//                 return ValueTask.CompletedTask;
//             }
//
//             if (!_channel.Writer.TryWrite(e) && Stop(StopReason.QueueOverflow))
//                 logger?.SubscriptionQueueOverflowed(subscriptionId, queueCapacity);
//
//             return ValueTask.CompletedTask;
//         }
//
//         public ValueTask OnResetAsync(CancellationToken cancellationToken)
//         {
//             throw new NotImplementedException();
//         }
//
//         public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
//         {
//             throw new NotImplementedException();
//         }
//
//         public void Dispose()
//         {
//             throw new NotImplementedException();
//         }
//
//         private bool Stop(StopReason reason)
//         {
//             _isStopped = true;
//
//             var previous = Interlocked.CompareExchange(ref _stopReason, (int)reason, (int)StopReason.None);
//             if (previous != (int)StopReason.None)
//                 return false;
//
//             _channel.Writer.TryComplete();
//             return true;
//         }
//     }
//
//     internal enum StopReason
//     {
//         None,
//         QueueOverflow,
//         FeedReset,
//         Disposed
//     }
// }

