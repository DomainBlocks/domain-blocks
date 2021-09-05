using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections
{
    public sealed class EventProjectionBuilder<TEvent> : IEventProjectionBuilder
    {
        private readonly ProjectionRegistryBuilder _builder;
        private readonly HashSet<IProjectionBuilder> _projectionBuilders = new();

        public EventProjectionBuilder(ProjectionRegistryBuilder builder)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public EventProjectionBuilder<TEvent> FromName(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            _builder.RegisterEventName<TEvent>(name);
            return this;
        }

        public EventProjectionBuilder<TEvent> FromNames(params string[] names)
        {
            foreach (var name in names)
            {
                _builder.RegisterEventName<TEvent>(name);
            }

            return this;
        }

        public void RegisterContextForEvent(IProjectionContext projectionContext)
        {
            if (projectionContext == null) throw new ArgumentNullException(nameof(projectionContext));
            _builder.RegisterContextForEvent<TEvent>(projectionContext);
        }

        public void RegisterProjectionBuilder(IProjectionBuilder projectionBuilder)
        {
            if (projectionBuilder == null) throw new ArgumentNullException(nameof(projectionBuilder));
            if (!_projectionBuilders.Add(projectionBuilder))
            {
                throw new
                    InvalidOperationException($"Projection Builder {projectionBuilder.GetType().FullName} has already been registered");
            }
        }

        public IEnumerable<(Type eventType, Type projectionType, RunProjection func)> BuildProjectionFuncs()
        {
            return _projectionBuilders.SelectMany(pb => pb.BuildProjections());
        }
    }
}