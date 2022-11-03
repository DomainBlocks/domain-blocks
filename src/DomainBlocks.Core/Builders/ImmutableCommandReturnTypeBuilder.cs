using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Core.Builders;

public interface IImmutableCommandReturnUpdatedStateSelectorBuilder<in TAggregate, out TCommandResult>
{
    public void WithUpdatedStateFrom(Func<TCommandResult, TAggregate> updatedStateSelector);
    public void ApplyEvents();
}

public class ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase> where TEventBase : class
{
    private readonly List<ICommandReturnTypeBuilder> _builders = new();

    internal IEnumerable<ICommandReturnType> Options => _builders.Select(x => x.Options);

    public ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult>
        CommandReturnType<TCommandResult>()
    {
        var builder = new ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult>();
        _builders.Add(builder);
        return builder;
    }
}

public class ImmutableCommandReturnTypeBuilder<TAggregate, TEventBase, TCommandResult> :
    ICommandReturnTypeBuilder,
    IImmutableCommandReturnUpdatedStateSelectorBuilder<TAggregate, TCommandResult>
    where TEventBase : class
{
    private ImmutableCommandReturnType<TAggregate, TEventBase, TCommandResult> _options = new();

    public ICommandReturnType Options => _options;

    public IImmutableCommandReturnUpdatedStateSelectorBuilder<TAggregate, TCommandResult> WithEventsFrom(
        Func<TCommandResult, IEnumerable<TEventBase>> eventsSelector)
    {
        _options = _options.WithEventSelector(eventsSelector);
        return this;
    }

    void IImmutableCommandReturnUpdatedStateSelectorBuilder<TAggregate, TCommandResult>.WithUpdatedStateFrom(
        Func<TCommandResult, TAggregate> updatedStateSelector)
    {
        _options = _options.WithUpdatedStateSelector(updatedStateSelector);
    }

    void IImmutableCommandReturnUpdatedStateSelectorBuilder<TAggregate, TCommandResult>.ApplyEvents()
    {
        _options = _options.WithUpdatedStateFromEvents();
    }
}