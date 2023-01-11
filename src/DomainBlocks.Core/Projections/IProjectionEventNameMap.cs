namespace DomainBlocks.Core.Projections;

public interface IProjectionEventNameMap
{
    IEnumerable<Type> GetClrTypesForEventName(string eventName);
}