namespace DomainBlocks.EventStore.Abstractions.New;

internal sealed class EventSubscriptionBuilder<TEvent, TPos>(
    Func<SubscriptionDefinition<TPos>, IAsyncEnumerable<ISubscriptionMessage>> subscriber,
    SubscriptionOptions options) :
    IEventSubscriptionBuilder<TEvent, TPos>,
    IEventSubscriptionBuilder<TEvent, TPos>.ISubscription,
    IEventSubscriptionBuilder<TEvent, TPos>.ISubscriptionMessages
    where TEvent : notnull
    where TPos : notnull
{
    private SubscriptionDefinition<TPos>? _definition;

    // ─────────────────────────────────────────────────────────────
    // IEventSubscriptionBuilder<TEvent, TPos>
    // ─────────────────────────────────────────────────────────────

    IEventSubscriptionBuilder<TEvent, TPos>.ISubscription IEventSubscriptionBuilder<TEvent, TPos>.FromStart()
    {
        _definition = new SubscriptionDefinition<TPos>(ReadOrigin.Start<TPos>(), options);
        return this;
    }

    IEventSubscriptionBuilder<TEvent, TPos>.ISubscription IEventSubscriptionBuilder<TEvent, TPos>.From(TPos position)
    {
        _definition = new SubscriptionDefinition<TPos>(ReadOrigin.From(position), options);
        return this;
    }

    IEventSubscriptionBuilder<TEvent, TPos>.ISubscription IEventSubscriptionBuilder<TEvent, TPos>.After(TPos position)
    {
        _definition = new SubscriptionDefinition<TPos>(ReadOrigin.After(position), options);
        return this;
    }

    IEventSubscriptionBuilder<TEvent, TPos>.ISubscription IEventSubscriptionBuilder<TEvent, TPos>.FromLive()
    {
        _definition = new SubscriptionDefinition<TPos>(ReadOrigin.End<TPos>(), options);
        return this;
    }

    // ─────────────────────────────────────────────────────────────
    // IEventSubscriptionBuilder<TEvent, TPos>.ISubscription
    // ─────────────────────────────────────────────────────────────

    IAsyncEnumerable<TEvent> IEventSubscriptionBuilder<TEvent, TPos>.ISubscription.ToAsyncEnumerable()
    {
        return subscriber(_definition!)
            .OfType<SubscriptionMessage.EventReceived<TEvent>>()
            .Select(x => x.Event);
    }

    IEventSubscriptionBuilder<TEvent, TPos>.ISubscriptionMessages
        IEventSubscriptionBuilder<TEvent, TPos>.ISubscription.AsMessages() => this;

    // ─────────────────────────────────────────────────────────────
    // IEventSubscriptionBuilder<TEvent, TPos>.ISubscriptionMessages
    // ─────────────────────────────────────────────────────────────

    IAsyncEnumerable<ISubscriptionMessage>
        IEventSubscriptionBuilder<TEvent, TPos>.ISubscriptionMessages.ToAsyncEnumerable() => subscriber(_definition!);
}