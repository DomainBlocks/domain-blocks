using System;
using System.Collections.Generic;

namespace DomainBlocks.Serialization
{
    public interface IEventSerializer<TRawData>
    {
        IEventPersistenceData<TRawData> GetPersistenceData(object @event, string eventNameOverride = null, params KeyValuePair<string, string>[] additionalMetadata);
        TEvent DeserializeEvent<TEvent>(TRawData eventData, string eventName, Type typeOverride = null);
        void UseMetaDataContext(EventMetadataContext metadataContext);
        Dictionary<string, string> DeserializeMetadata(TRawData rawMetadata);
    }
}