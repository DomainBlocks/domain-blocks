using System.Collections.Generic;

namespace DomainLib.Serialization
{
    public interface IEventSerializer
    {
        IEventPersistenceData GetPersistenceData(object @event, params KeyValuePair<string, string>[] additionalMetadata);
        TEvent DeserializeEvent<TEvent>(byte[] eventData, string eventName);
        void UseMetaDataContext(EventMetadataContext metadataContext);
        Dictionary<string, string> DeserializeMetadata(byte[] metadataBytes);
    }
}