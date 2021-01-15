namespace DomainLib.Serialization
{
    public class ByteArrayEventSerializationAdapter : IEventSerializationAdapter<byte[]>
    {
        public byte[] FromRawData(byte[] rawEventData)
        {
            return rawEventData;
        }

        public byte[] ToRawData(byte[] bytes)
        {
            return bytes;
        }
    }
}