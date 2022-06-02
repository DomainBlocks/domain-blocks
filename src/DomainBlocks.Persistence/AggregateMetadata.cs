using System;

namespace DomainBlocks.Persistence;

internal sealed class AggregateMetadata
{
    public Func<object, object> InitialStateFactory { get; set; }
    public Func<object, string> IdSelector { get; set; }
    public Func<string, string> IdToKeySelector { get; set; }
    public Func<string, string> IdToSnapshotKeySelector { get; set; }
    public Func<object, string> KeySelector => agg => IdToKeySelector(IdSelector(agg));
    public Func<object, string> SnapshotKeySelector => agg => IdToSnapshotKeySelector(IdSelector(agg));
}