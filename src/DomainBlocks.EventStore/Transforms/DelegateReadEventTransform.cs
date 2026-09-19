namespace DomainBlocks.EventStore.Transforms;

internal sealed class DelegateReadEventTransform<TEventBase, TSourceEvent>(
    Func<TSourceEvent, ReadEventInfo, IEnumerable<TEventBase>> apply) :
    ReadEventTransform<TEventBase, TSourceEvent>
    where TEventBase : notnull
    where TSourceEvent : TEventBase
{
    protected override IEnumerable<TEventBase> Apply(TSourceEvent @event, ReadEventInfo info) => apply(@event, info);
}