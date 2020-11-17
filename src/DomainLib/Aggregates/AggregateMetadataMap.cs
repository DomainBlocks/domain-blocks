using System;
using System.Collections.Generic;

namespace DomainLib.Aggregates
{
    public sealed class AggregateMetadataMap : Dictionary<Type, AggregateMetadata>
    {
        public AggregateMetadata GetForInstance<TAggregate>(TAggregate instance)
        {
            var aggregateType = instance.GetType();
            return GetForType(aggregateType);
        }

        public AggregateMetadata GetForType<TAggregate>()
        {
            return GetForType(typeof(TAggregate));
        }

        private AggregateMetadata GetForType(Type aggregateType)
        {
            if (TryGetValue(aggregateType, out var metadata))
            {
                return metadata;
            }

            var message = $"No metadata found for aggregate {aggregateType.Name}";
            throw new InvalidOperationException(message);
        }
    }
}