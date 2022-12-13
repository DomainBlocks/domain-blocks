using System;
using DomainBlocks.Core;

namespace DomainBlocks.Persistence;

public class AggregateRepositoryOptions
{
    private Func<Model, IAggregateRepository> _aggregateRepositoryFactory;
    private Func<IEventNameMap, ISnapshotRepository> _snapshotRepositoryFactory;

    public AggregateRepositoryOptions()
    {
    }

    private AggregateRepositoryOptions(AggregateRepositoryOptions copyFrom)
    {
        _aggregateRepositoryFactory = copyFrom._aggregateRepositoryFactory;
        _snapshotRepositoryFactory = copyFrom._snapshotRepositoryFactory;
    }

    public AggregateRepositoryOptions WithAggregateRepositoryFactory(Func<Model, IAggregateRepository> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        return new AggregateRepositoryOptions(this) { _aggregateRepositoryFactory = factory };
    }

    public AggregateRepositoryOptions WithSnapshotRepositoryFactory(Func<IEventNameMap, ISnapshotRepository> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        return new AggregateRepositoryOptions(this) { _snapshotRepositoryFactory = factory };
    }

    public IAggregateRepository CreateAggregateRepository(Model model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        if (_aggregateRepositoryFactory == null)
        {
            throw new InvalidOperationException("Cannot create aggregate repository as no factory has been specified.");
        }

        return _aggregateRepositoryFactory(model);
    }

    public ISnapshotRepository CreateSnapshotRepository(IEventNameMap eventNameMap)
    {
        if (_snapshotRepositoryFactory == null)
        {
            throw new InvalidOperationException("Cannot create snapshot repository as no factory has been specified.");
        }

        return _snapshotRepositoryFactory(eventNameMap);
    }
}