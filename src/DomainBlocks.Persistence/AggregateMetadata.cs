using System;
using DomainBlocks.Aggregates;

namespace DomainBlocks.Persistence;

public sealed class AggregateMetadata
{
    public Func<IAggregateEventRouter, object> InitialStateFactory { get; internal set; }
    public Func<object, string> IdSelector { get; internal set; }
    public Func<string, string> IdToKeySelector { get; internal set; }
    public Func<string, string> IdToSnapshotKeySelector { get; internal set; }
    public Func<object, string> KeySelector => agg => IdToKeySelector(IdSelector(agg));
    public Func<object, string> SnapshotKeySelector => agg => IdToSnapshotKeySelector(IdSelector(agg));
}