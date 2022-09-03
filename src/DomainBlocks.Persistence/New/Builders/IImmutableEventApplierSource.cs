using System;

namespace DomainBlocks.Persistence.New.Builders;

internal interface IImmutableEventApplierSource<TAggregate, in TEventBase>
{
    public Func<TAggregate, TEventBase, TAggregate> EventApplier { get; }
}