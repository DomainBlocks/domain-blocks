using System.Linq.Expressions;

namespace DomainBlocks.Core;

public abstract class EventSourcedEntityTypeBase<TEntity> : IEventSourcedEntityType<TEntity>
{
    private static readonly Lazy<Func<TEntity>> DefaultFactory = new(() => GetDefaultFactory());
    private static readonly Lazy<Func<TEntity, string>> DefaultIdSelector = new(GetDefaultIdSelector);
    private Func<TEntity> _factory;
    private Func<TEntity, string> _idSelector;
    private Func<string, string> _idToStreamKeySelector;
    private Func<string, string> _idToSnapshotKeySelector;

    protected EventSourcedEntityTypeBase()
    {
        _factory = DefaultFactory.Value;
        _idSelector = DefaultIdSelector.Value;
        var prefix = GetDefaultKeyPrefix();
        _idToStreamKeySelector = GetIdToStreamKeySelector(prefix);
        _idToSnapshotKeySelector = GetIdToSnapshotKeySelector(prefix);
    }

    protected EventSourcedEntityTypeBase(EventSourcedEntityTypeBase<TEntity> copyFrom)
    {
        if (copyFrom == null) throw new ArgumentNullException(nameof(copyFrom));

        _factory = copyFrom._factory;
        _idSelector = copyFrom._idSelector;
        _idToStreamKeySelector = copyFrom._idToStreamKeySelector;
        _idToSnapshotKeySelector = copyFrom._idToSnapshotKeySelector;
    }

    public Type ClrType => typeof(TEntity);
    public abstract IEnumerable<IEventType> EventTypes { get; }

    public TEntity CreateNew()
    {
        if (_factory == null)
        {
            throw new InvalidOperationException(
                "Cannot create new entity instance as no factory has been specified.");
        }

        return _factory();
    }

    public string GetId(TEntity aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));

        if (_idSelector == null)
        {
            throw new InvalidOperationException("Cannot get ID as no ID selector has been specified.");
        }

        return _idSelector(aggregate);
    }

    public string MakeStreamKey(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("ID cannot be null or whitespace", nameof(id));

        return _idToStreamKeySelector(id);
    }

    public string MakeSnapshotKey(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("ID cannot be null or whitespace", nameof(id));

        return _idToSnapshotKeySelector(id);
    }

    public string MakeSnapshotKey(TEntity aggregate)
    {
        return MakeSnapshotKey(GetId(aggregate));
    }

    public EventSourcedEntityTypeBase<TEntity> SetFactory(Func<TEntity> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));

        var clone = Clone();
        clone._factory = factory;
        return clone;
    }

    public EventSourcedEntityTypeBase<TEntity> SetIdSelector(Func<TEntity, string> idSelector)
    {
        if (idSelector == null) throw new ArgumentNullException(nameof(idSelector));

        var clone = Clone();
        clone._idSelector = idSelector;
        return clone;
    }

    public EventSourcedEntityTypeBase<TEntity> SetKeyPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix cannot be null or whitespace.", nameof(prefix));

        var clone = Clone();
        clone._idToStreamKeySelector = GetIdToStreamKeySelector(prefix);
        clone._idToSnapshotKeySelector = GetIdToSnapshotKeySelector(prefix);
        return clone;
    }

    public EventSourcedEntityTypeBase<TEntity> SetIdToStreamKeySelector(
        Func<string, string> idToStreamKeySelector)
    {
        if (idToStreamKeySelector == null) throw new ArgumentNullException(nameof(idToStreamKeySelector));

        var clone = Clone();
        clone._idToStreamKeySelector = idToStreamKeySelector;
        return clone;
    }

    public EventSourcedEntityTypeBase<TEntity> SetIdToSnapshotKeySelector(
        Func<string, string> idToSnapshotKeySelector)
    {
        if (idToSnapshotKeySelector == null) throw new ArgumentNullException(nameof(idToSnapshotKeySelector));

        var clone = Clone();
        clone._idToSnapshotKeySelector = idToSnapshotKeySelector;
        return clone;
    }

    protected abstract EventSourcedEntityTypeBase<TEntity> Clone();

    private static Func<TEntity> GetDefaultFactory()
    {
        var ctor = typeof(TEntity).GetConstructor(Type.EmptyTypes);
        if (ctor == null)
        {
            return () => throw new InvalidOperationException(
                "No factory function specified, and no default constructor found");
        }

        var newExpr = Expression.New(ctor);
        var lambda = Expression.Lambda<Func<TEntity>>(newExpr);
        return lambda.Compile();
    }

    private static Func<TEntity, string> GetDefaultIdSelector()
    {
        const string defaultIdPropertyName = "Id";

        var idSelector =
            (GetIdSelector(defaultIdPropertyName) ??
             GetIdSelector($"{typeof(TEntity).Name}{defaultIdPropertyName}")) ??
            (_ => throw new InvalidOperationException("No ID selector specified, and no suitable ID property found"));

        return idSelector;
    }

    private static Func<TEntity, string>? GetIdSelector(string propertyName)
    {
        var property = typeof(TEntity).GetProperty(propertyName);
        if (property == null)
        {
            return null;
        }

        var aggregateParam = Expression.Parameter(typeof(TEntity));
        var propertyExpr = Expression.Property(aggregateParam, property);
        var asString = Expression.Call(propertyExpr, nameof(ToString), null);
        var lambda = Expression.Lambda<Func<TEntity, string>>(asString, aggregateParam);
        return lambda.Compile();
    }

    private static string GetDefaultKeyPrefix()
    {
        var name = typeof(TEntity).Name;
        return $"{name[..1].ToLower()}{name[1..]}";
    }

    private static Func<string, string> GetIdToStreamKeySelector(string prefix)
    {
        return id => $"{prefix}-{id}";
    }

    private static Func<string, string> GetIdToSnapshotKeySelector(string prefix)
    {
        return id => $"{prefix}Snapshot-{id}";
    }
}