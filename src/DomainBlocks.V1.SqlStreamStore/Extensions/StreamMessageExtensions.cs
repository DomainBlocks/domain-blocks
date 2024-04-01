using System.Text;
using DomainBlocks.V1.Abstractions;
using StreamVersion = DomainBlocks.V1.Abstractions.StreamVersion;
using SqlStreamStoreStreamMessage = DomainBlocks.ThirdParty.SqlStreamStore.Streams.StreamMessage;

namespace DomainBlocks.V1.SqlStreamStore.Extensions;

internal static class StreamMessageExtensions
{
    public static async Task<ReadEvent> ToReadEvent(
        this SqlStreamStoreStreamMessage message, CancellationToken cancellationToken)
    {
        var payload = await message.GetJsonData(cancellationToken);
        var payloadAsBytes = Encoding.UTF8.GetBytes(payload);
        var metadataAsBytes =
            message.JsonMetadata == null ? null : Encoding.UTF8.GetBytes(message.JsonMetadata);
        var streamVersion = StreamVersion.FromInt32(message.StreamVersion);
        var globalPosition = GlobalPosition.FromInt64(message.Position);

        return new ReadEvent(message.Type, payloadAsBytes, metadataAsBytes, streamVersion, globalPosition);
    }
}