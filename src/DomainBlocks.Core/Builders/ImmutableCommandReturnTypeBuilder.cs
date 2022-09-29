using System;
using System.Collections.Generic;

namespace DomainBlocks.Core.Builders;

public interface IImmutableCommandReturnUpdatedStateSelectorBuilder<in TAggregate, out TCommandResult>
{
    public void WithUpdatedStateFrom(Func<TCommandResult, TAggregate> updatedStateSelector);
    public void ApplyEvents();
}

public class ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase> where TEventBase : class
{
    private readonly ICollection<ICommandReturnTypeBuilder> _builders;
    private readonly IImmutableEventApplierSource<TAggregate, TEventBase> _eventApplierSource;

    internal ImmutableCommandReturnTypeBuilder(
        ICollection<ICommandReturnTypeBuilder> builders,
        IImmutableEventApplierSource<TAggregate, TEventBase> eventApplierSource)
    {
        _builders = builders;
        _eventApplierSource = eventApplierSource;
    }

    public ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult> CommandReturnType<TCommandResult>()
    {
        var builder =
            new ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult>(_eventApplierSource);
        _builders.Add(builder);
        return builder;
    }
}

public class ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandReturnTypeBuilder,
    IImmutableCommandReturnUpdatedStateSelectorBuilder<TAggregate, TCommandResult>
    where TEventBase : class
{
    private readonly IImmutableEventApplierSource<TAggregate, TEventBase> _eventApplierSource;
    private Func<TCommandResult, IEnumerable<TEventBase>> _eventsSelector;
    private Func<TCommandResult, TAggregate> _updatedStateSelector;
    private bool _hasApplyEventsEnabled;

    internal ImmutableCommandReturnTypeBuilder(IImmutableEventApplierSource<TAggregate, TEventBase> eventApplierSource)
    {
        _eventApplierSource = eventApplierSource ?? throw new ArgumentNullException(nameof(eventApplierSource));
    }

    public IImmutableCommandReturnUpdatedStateSelectorBuilder<TAggregate, TCommandResult> WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _eventsSelector = eventsSelector ?? throw new ArgumentNullException(nameof(eventsSelector));
        return this;
    }

    void IImmutableCommandReturnUpdatedStateSelectorBuilder<TAggregate, TCommandResult>.WithUpdatedStateFrom(
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        _updatedStateSelector = updatedStateSelector ?? throw new ArgumentNullException(nameof(updatedStateSelector));
    }

    void IImmutableCommandReturnUpdatedStateSelectorBuilder<TAggregate, TCommandResult>.ApplyEvents()
    {
        _hasApplyEventsEnabled = true;
    }

    public ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult> Build()
    {
        return _hasApplyEventsEnabled
            ? new ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(
                _eventsSelector, _eventApplierSource.EventApplier)
            : new ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult>(
                _eventsSelector, _updatedStateSelector);
    }

    ICommandReturnType ICommandReturnTypeBuilder.Build() => Build();
}