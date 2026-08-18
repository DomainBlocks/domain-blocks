// using System.Diagnostics;
// using System.Runtime.CompilerServices;
// using DomainBlocks.EventStore.Abstractions;
// using DomainBlocks.EventStore.Abstractions.Codecs;
// using KurrentDB.Client;
// using StreamNotFoundException = DomainBlocks.EventStore.Abstractions.StreamNotFoundException;
// using StreamPosition = KurrentDB.Client.StreamPosition;
// using StreamState = KurrentDB.Client.StreamState;
//
// namespace DomainBlocks.EventStore.KurrentDB;
//
// public class KurrentDBEventStoreClient2<TEvent>(
//     KurrentDBClient client,
//     IEventEncoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventEncoder,
//     IEventDecoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventDecoder) :
//     IEventStoreClient2<TEvent, string, StreamPosition, Position>
//     where TEvent : notnull
// {
//     public async Task AppendToStreamAsync(
//         string streamId,
//         ExpectedStreamState2<StreamPosition> expectedStreamState,
//         IEnumerable<AppendEvent<TEvent>> events,
//         AppendToStreamOptions? options = null,
//         CancellationToken cancellationToken = default)
//     {
//         options ??= new AppendToStreamOptions();
//         var kurrentExpectedState = ToKurrentStreamState(expectedStreamState);
//
//         var eventData = eventEncoder
//             .Encode(events)
//             .Select(x => new EventData(Uuid.NewUuid(), x.EventName, x.EventData, x.Metadata));
//
//         try
//         {
//             _ = await client
//                 .AppendToStreamAsync(
//                     streamId,
//                     kurrentExpectedState,
//                     eventData,
//                     cancellationToken: cancellationToken)
//                 .ConfigureAwait(false);
//         }
//         catch (WrongExpectedVersionException ex)
//         {
//             //var actualState = ToStreamState(ex.ActualStreamState);
//             //throw new StreamAppendConflictException(streamId, options.ExpectedStreamState, actualState, ex);
//             throw;
//         }
//     }
//
//     public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, Position>> ReadAll(
//         ReadMode<Position>? mode = null,
//         ReadAllOptions? options = null)
//     {
//         throw new NotImplementedException();
//     }
//
//     public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, Position>> ReadStream(
//         string streamId,
//         ReadMode<StreamPosition>? mode = null,
//         ReadStreamOptions? options = null)
//     {
//         return ReadStreamCoreAsync(streamId, mode, options);
//     }
//
//     public IAsyncEnumerable<ReadNotification<>> SubscribeToAll(
//         SubscribeOrigin<Position>? origin = null,
//         SubscribeToAllOptions? options = null)
//     {
//         throw new NotImplementedException();
//     }
//
//     public IAsyncEnumerable<ReadNotification<>> SubscribeToStream(
//         string streamId,
//         SubscribeOrigin<StreamPosition>? origin = null,
//         SubscribeToStreamOptions? options = null)
//     {
//         throw new NotImplementedException();
//     }
//
//     public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, Position>> ReadLog(
//         ReadDefinition<Position> definition)
//     {
//         throw new NotImplementedException();
//     }
//
//     private static StreamState ToKurrentStreamState(ExpectedStreamState2<StreamPosition> expected) => expected switch
//     {
//         { Kind: ExpectedStreamStateKind.Any } => StreamState.Any,
//         { Kind: ExpectedStreamStateKind.DoesNotExist } => StreamState.NoStream,
//         { Kind: ExpectedStreamStateKind.Exists } => StreamState.StreamExists,
//         { Kind: ExpectedStreamStateKind.AtVersion } => StreamState.StreamRevision(expected.Version),
//         _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, null)
//     };
//
//     private static StreamState2<StreamPosition>? ToStreamState(StreamState kurrentStreamState)
//     {
//         if (kurrentStreamState == StreamState.NoStream)
//             return StreamState2<StreamPosition>.DoesNotExist;
//
//         if (kurrentStreamState.HasPosition)
//         {
//             var value = kurrentStreamState.ToInt64();
//             if (value >= 0)
//                 return StreamState2<StreamPosition>.AtVersion(StreamPosition.FromInt64(value));
//         }
//
//         return null;
//     }
//
//     private async IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, Position>> ReadStreamCoreAsync(
//         string streamId,
//         ReadMode<StreamPosition>? mode,
//         ReadStreamOptions? options,
//         [EnumeratorCancellation] CancellationToken cancellationToken = default)
//     {
//         mode ??= ReadMode.ForwardFrom(StreamPosition.Start);
//         options ??= ReadStreamOptions.Default;
//
//         var isEmptyEnumeration = mode is ReadMode<StreamPosition>.ForwardFrom m1 &&
//                                  m1.Position == StreamPosition.End ||
//                                  mode is ReadMode<StreamPosition>.BackwardFrom m2 &&
//                                  m2.Position == StreamPosition.Start;
//
//         if (isEmptyEnumeration)
//         {
//             if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
//                 !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
//             {
//                 throw new StreamNotFoundException(streamId);
//             }
//
//             yield break;
//         }
//
//         var (direction, revision) = mode switch
//         {
//             ReadMode<StreamPosition>.ForwardFromStart => (Direction.Forwards, StreamPosition.Start),
//             ReadMode<StreamPosition>.BackwardFromEnd => (Direction.Backwards, StreamPosition.End),
//             ReadMode<StreamPosition>.ForwardFrom m => (Direction.Forwards, m.Position),
//             ReadMode<StreamPosition>.BackwardFrom m => (Direction.Backwards, m.Position),
//             _ => throw new UnreachableException($"Unknown ReadMode '{mode.GetType().Name}'.")
//         };
//
//         var result = client.ReadStreamAsync(
//             direction,
//             streamId,
//             revision,
//             cancellationToken: cancellationToken);
//
//         if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
//             await result.ReadState.ConfigureAwait(false) == ReadState.StreamNotFound)
//         {
//             throw new StreamNotFoundException(streamId);
//         }
//
//         await foreach (var resolvedEvent in result.ConfigureAwait(false))
//         {
//             var record = resolvedEvent.Event;
//             var originalRecord = resolvedEvent.OriginalEvent;
//             var metadataBytes = options.IncludeMetadata ? record.Metadata : default;
//
//             var (@event, metadata) = eventDecoder.Decode(record.EventType, record.Data, metadataBytes);
//
//             yield return ReadEvent2.Create(
//                 @event,
//                 streamId,
//                 metadata,
//                 record.Created,
//                 originalRecord.EventNumber,
//                 originalRecord.Position);
//         }
//     }
//
//     private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
//     {
//         var streamExist = client.ReadStreamAsync(
//             Direction.Backwards,
//             streamId,
//             StreamPosition.End,
//             maxCount: 1,
//             cancellationToken: cancellationToken);
//
//         var readState = await streamExist.ReadState.ConfigureAwait(false);
//
//         return readState == ReadState.Ok;
//     }
// }

