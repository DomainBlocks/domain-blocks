using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore;

internal sealed class EventSubscriptionBuilder<TEvent, TPos>(
    Func<SubscriptionDefinition<TPos>, IAsyncEnumerable<SubscriptionMessage>> subscriber) :
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
        _definition = SubscriptionDefinition.FromStart<TPos>();
        return this;
    }

    IEventSubscriptionBuilder<TEvent, TPos>.ISubscription IEventSubscriptionBuilder<TEvent, TPos>.After(TPos position)
    {
        _definition = SubscriptionDefinition.After(position);
        return this;
    }

    IEventSubscriptionBuilder<TEvent, TPos>.ISubscription IEventSubscriptionBuilder<TEvent, TPos>.FromLive()
    {
        _definition = SubscriptionDefinition.FromLive<TPos>();
        return this;
    }

    // ─────────────────────────────────────────────────────────────
    // IEventSubscriptionBuilder<TEvent, TPos>.ISubscription
    // ─────────────────────────────────────────────────────────────

    IAsyncEnumerable<TEvent> IEventSubscriptionBuilder<TEvent, TPos>.ISubscription.ToAsyncEnumerable()
    {
        return subscriber(_definition!)
            .OfType<SubscriptionMessage.Event<TEvent>>()
            .Select(x => x.Value);
    }

    IEventSubscriptionBuilder<TEvent, TPos>.ISubscriptionMessages
        IEventSubscriptionBuilder<TEvent, TPos>.ISubscription.AsMessages() => this;

    // ─────────────────────────────────────────────────────────────
    // IEventSubscriptionBuilder<TEvent, TPos>.ISubscriptionMessages
    // ─────────────────────────────────────────────────────────────

    IAsyncEnumerable<SubscriptionMessage>
        IEventSubscriptionBuilder<TEvent, TPos>.ISubscriptionMessages.ToAsyncEnumerable() => subscriber(_definition!);
}