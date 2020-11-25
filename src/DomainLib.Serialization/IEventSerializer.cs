using System;
using System.Collections.Generic;

namespace DomainLib.Serialization
{
    public interface IEventSerializer
    {
        IEventPersistenceData  GetPersistenceData(object @event, string eventNameOverride = null, params KeyValuePair<string, string>[] additionalMetadata);
        TEvent DeserializeEvent<TEvent>(byte[] eventData, string eventName, Type typeOverride = null);
        void UseMetaDataContext(EventMetadataContext metadataContext);
        Dictionary<string, string> DeserializeMetadata(byte[] metadataBytes);
    }
}