namespace DomainLib.Serialization
{
    public interface IEventSerializationAdapter<TRawData>
    {
        byte[] FromRawData(TRawData rawEventData);
        TRawData ToRawData(byte[] bytes);
    }
}