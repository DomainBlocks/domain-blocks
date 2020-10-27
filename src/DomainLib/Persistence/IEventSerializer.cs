using DomainLib.Aggregates;

namespace DomainLib.Persistence
{
    public interface IEventSerializer
    {
        IEventPersistenceData GetPersistenceData(object @event);
        TEvent DeserializeEvent<TEvent>(byte[] eventData, string eventName);
        void RegisterEventTypeMappings(IEventNameMap eventNameMap);
    }
}