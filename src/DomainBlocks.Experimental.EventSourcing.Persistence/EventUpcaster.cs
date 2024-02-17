namespace DomainBlocks.Experimental.EventSourcing.Persistence;

public sealed class EventUpcaster
{
    private readonly Func<object, object> _upcastFunc;

    private EventUpcaster(Type sourceEventType, Type targetEventType, Func<object, object> upcastFunc)
    {
        SourceEventType = sourceEventType;
        TargetEventType = targetEventType;
        _upcastFunc = upcastFunc;
    }

    public Type SourceEventType { get; }
    public Type TargetEventType { get; }

    public static EventUpcaster Create<TSourceEvent, TTargetEvent>(Func<TSourceEvent, TTargetEvent> upcastFunc)
    {
        if (upcastFunc == null) throw new ArgumentNullException(nameof(upcastFunc));

        return new EventUpcaster(
            typeof(TSourceEvent),
            typeof(TTargetEvent),
            sourceEvent => upcastFunc((TSourceEvent)sourceEvent)!);
    }

    public object Invoke(object sourceEvent)
    {
        if (sourceEvent == null) throw new ArgumentNullException(nameof(sourceEvent));

        try
        {
            return _upcastFunc(sourceEvent);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Error invoking upcast function for event type '{sourceEvent.GetType()}'.", ex);
        }
    }
}