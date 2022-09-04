using System;

namespace DomainBlocks.Core.Builders;

internal interface IMutableEventApplierSource<in TAggregate, in TEventBase>
{
    public Action<TAggregate, TEventBase> EventApplier { get; }
}