using System;

namespace DomainBlocks.Core.Builders;

internal interface IImmutableEventApplierSource<TAggregate, in TEventBase>
{
    public Func<TAggregate, TEventBase, TAggregate> EventApplier { get; }
}