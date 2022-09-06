using System;

namespace DomainBlocks.Core.Builders;

public interface IMutableEventApplierSource<in TAggregate, in TEventBase>
{
    public Action<TAggregate, TEventBase> EventApplier { get; }
}