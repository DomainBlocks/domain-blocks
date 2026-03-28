using System.Runtime.InteropServices;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public static class NonConcurrentPool
{
    public static NonConcurrentPool<T> Create<T>() where T : class, new()
    {
        return new NonConcurrentPool<T>(() => new T());
    }

    public static NonConcurrentPool<T> Create<T>(Func<T> factory) where T : class
    {
        return new NonConcurrentPool<T>(factory);
    }
}

public sealed class NonConcurrentPool<T>(Func<T> factory) where T : class
{
    private readonly List<T> _pool = [];
    private int _index;

    public T RentOne()
    {
        if (_pool.Count == _index)
            _pool.Add(factory());

        return _pool[_index++];
    }

    public ReadOnlySpan<T> RentMany(int count)
    {
        while (_pool.Count < _index + count)
            _pool.Add(factory());

        var span = CollectionsMarshal.AsSpan(_pool).Slice(_index, count);
        _index += count;

        return span;
    }

    public void ReturnAll() => _index = 0;
}