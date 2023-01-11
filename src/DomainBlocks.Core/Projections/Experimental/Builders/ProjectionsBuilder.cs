using DomainBlocks.Core.Subscriptions.Builders;

namespace DomainBlocks.Core.Projections.Experimental.Builders;

public sealed class ProjectionsBuilder<TEvent, TPosition>
    where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
{
    private readonly EventStreamSubscriberBuilder<TEvent, TPosition> _subscriberBuilder;

    internal ProjectionsBuilder(EventStreamSubscriberBuilder<TEvent, TPosition> subscriberBuilder)
    {
        _subscriberBuilder = subscriberBuilder;
    }

    public EventHandlerProjectionOptionsBuilder<TEvent, TPosition> EventHandlers()
    {
        var builder = new EventHandlerProjectionOptionsBuilder<TEvent, TPosition>(_subscriberBuilder);
        AddSubscriberBuilder(builder);

        return builder;
    }

    public StateProjectionOptionsBuilder<TEvent, TPosition, TState> SingletonState<TState>(TState state)
        where TState : class
    {
        var options = new StateProjectionOptions<TEvent, TPosition, TState>().WithSingletonState(state);
        var builder = new StateProjectionOptionsBuilder<TEvent, TPosition, TState>(_subscriberBuilder, options);
        AddSubscriberBuilder(builder);

        return builder;
    }

    public StateProjectionOptionsBuilder<TEvent, TPosition, TState> SingletonState<TState>(Func<TState> stateFactory)
        where TState : class
    {
        var options = new StateProjectionOptions<TEvent, TPosition, TState>()
            .WithStateFactory(stateFactory)
            .WithStateLifetime(ProjectionStateLifetime.Singleton);

        var builder = new StateProjectionOptionsBuilder<TEvent, TPosition, TState>(_subscriberBuilder, options);
        AddSubscriberBuilder(builder);

        return builder;
    }

    public StateProjectionOptionsBuilder<TEvent, TPosition, TState> State<TState>(
        Func<SubscriptionStatus, TState> stateFactory) where TState : class
    {
        var options = new StateProjectionOptions<TEvent, TPosition, TState>().WithStateFactory(stateFactory);
        var builder = new StateProjectionOptionsBuilder<TEvent, TPosition, TState>(_subscriberBuilder, options);
        AddSubscriberBuilder(builder);

        return builder;
    }

    public StateProjectionOptionsBuilder<TEvent, TPosition, TState> State<TResource, TState>(
        Func<TResource> resourceFactory,
        Func<TResource, SubscriptionStatus, TState> stateFactory)
        where TResource : IDisposable
        where TState : class
    {
        var options = new StateProjectionOptions<TEvent, TPosition, TState>()
            .WithStateFactory(resourceFactory, stateFactory);

        var builder = new StateProjectionOptionsBuilder<TEvent, TPosition, TState>(_subscriberBuilder, options);
        AddSubscriberBuilder(builder);

        return builder;
    }

    public void Process()
    {
        // When an event appears:
        // 1. Get process ID from event
        // 2. Load process - get already-processed events from process  stream.
        // 3. Invoke "Transition" method for the event - here we can check if event was already processed.
        // 4. Dispatch/handle any commands
        // 5. Save process - i.e. write out event to process stream.

        throw new NotImplementedException();
    }

    private void AddSubscriberBuilder(IEventStreamSubscriberBuilder<TEvent, TPosition> builder)
    {
        ((IEventStreamSubscriberBuilderInfrastructure<TEvent, TPosition>)_subscriberBuilder)
            .AddSubscriberBuilder(builder);
    }
}