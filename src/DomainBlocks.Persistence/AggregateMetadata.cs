using System;

namespace DomainBlocks.Persistence
{
    public sealed class AggregateMetadata
    {
        public Func<object> GetInitialState { get; internal set; }
        public Func<object, string> GetIdentifier { get; internal set; }
        public Func<string, string> GetKeyFromIdentifier { get; internal set; }
        public Func<string, string> GetSnapshotKeyFromIdentifier { get; internal set; }
        public Func<object, string> GetKeyFromAggregate => agg => GetKeyFromIdentifier(GetIdentifier(agg));
        public Func<object, string> GetSnapshotKeyFromAggregate => agg => GetSnapshotKeyFromIdentifier(GetIdentifier(agg));
    }
}