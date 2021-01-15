using System.Text;

namespace DomainLib.Serialization
{
    public class Utf8StringEventSerializationAdapter : IEventSerializationAdapter<string>
    {
        public byte[] FromRawData(string rawEventData)
        {
            return Encoding.UTF8.GetBytes(rawEventData);
        }

        public string ToRawData(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
}