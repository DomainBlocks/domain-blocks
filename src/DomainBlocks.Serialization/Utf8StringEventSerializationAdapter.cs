using System;
using System.Text;

namespace DomainBlocks.Serialization
{
    public class Utf8StringEventSerializationAdapter : IEventSerializationAdapter<string>
    {
        public ReadOnlyMemory<byte> FromRawData(string rawEventData)
        {
            return Encoding.UTF8.GetBytes(rawEventData);
        }

        public string ToRawData(ReadOnlyMemory<byte> bytes)
        {
            return Encoding.UTF8.GetString(bytes.Span);
        }
    }
}