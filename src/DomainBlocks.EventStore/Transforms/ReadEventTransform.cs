namespace DomainBlocks.EventStore.Transforms;

public static class ReadEventTransform
{
    public static IReadEventTransform<TEventBase> Create<TEventBase, TSourceEvent>(
        Func<TSourceEvent, IEnumerable<TEventBase>> apply)
        where TEventBase : notnull
        where TSourceEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(apply);

        return new DelegateReadEventTransform<TEventBase, TSourceEvent>((e, _) => apply(e));
    }

    public static IReadEventTransform<TEventBase> Create<TEventBase, TSourceEvent>(
        Func<TSourceEvent, ReadEventInfo, IEnumerable<TEventBase>> apply)
        where TEventBase : notnull
        where TSourceEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(apply);

        return new DelegateReadEventTransform<TEventBase, TSourceEvent>(apply);
    }

    public static IReadEventTransform<TEventBase> Create<TEventBase, TSourceEvent>(
        Func<TSourceEvent, TEventBase> apply)
        where TEventBase : notnull
        where TSourceEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(apply);

        return new DelegateReadEventTransform<TEventBase, TSourceEvent>((e, _) => [apply(e)]);
    }

    public static IReadEventTransform<TEventBase> Create<TEventBase, TSourceEvent>(
        Func<TSourceEvent, ReadEventInfo, TEventBase> apply)
        where TEventBase : notnull
        where TSourceEvent : TEventBase
    {
        ArgumentNullException.ThrowIfNull(apply);

        return new DelegateReadEventTransform<TEventBase, TSourceEvent>((e, info) => [apply(e, info)]);
    }
}

public abstract class ReadEventTransform<TEventBase, TSourceEvent> : IReadEventTransform<TEventBase>
    where TEventBase : notnull
    where TSourceEvent : TEventBase
{
    public Type SourceEventType => typeof(TSourceEvent);

    protected abstract IEnumerable<TEventBase> Apply(TSourceEvent @event, ReadEventInfo info);

    IEnumerable<TEventBase> IReadEventTransform<TEventBase>.Apply<TStreamId, TStreamPos, TLogPos>(
        TEventBase @event,
        in ReadEventContext<TStreamId, TStreamPos, TLogPos> context)
    {
        return Apply((TSourceEvent)@event, new ReadEventInfo(context.Metadata, context.CreatedAt));
    }
}