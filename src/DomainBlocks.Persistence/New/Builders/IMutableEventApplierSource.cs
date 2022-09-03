using System;

namespace DomainBlocks.Persistence.New.Builders;

internal interface IMutableEventApplierSource<in TAggregate, in TEventBase>
{
    public Action<TAggregate, TEventBase> EventApplier { get; }
}