using System;

namespace DomainBlocks.Persistence.New.Builders;

internal interface IEventApplierSource<TAggregate, in TEventBase>
{
    public Func<TAggregate, TEventBase, TAggregate> EventApplier { get; }
}