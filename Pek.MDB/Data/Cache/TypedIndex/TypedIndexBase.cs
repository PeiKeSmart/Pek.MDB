using System.Collections.Concurrent;
using System.Globalization;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 抽象类型感知索引基类
/// </summary>
public abstract class TypedIndexBase : ITypedIndex
{
    protected readonly ConcurrentDictionary<object, HashSet<long>> _index = new();
    protected readonly object _lock = new();
    protected int _queryCount = 0;
    protected int _hitCount = 0;
    protected DateTime _lastAccessed = DateTime.Now;
    protected DateTime _createdTime = DateTime.Now;

    public virtual void AddId(object value, long id)
    {
        if (value == null) return;

        _index.AddOrUpdate(value,
            new HashSet<long> { id },
            (key, existingSet) =>
            {
                lock (_lock)
                {
                    existingSet.Add(id);
                }
                return existingSet;
            });
    }

    public virtual void RemoveId(object value, long id)
    {
        if (value == null) return;

        if (_index.TryGetValue(value, out var idSet))
        {
            lock (_lock)
            {
                idSet.Remove(id);
                if (idSet.Count == 0)
                {
                    _index.TryRemove(value, out _);
                }
            }
        }
    }

    public virtual HashSet<long> GetIds(object value)
    {
        if (value == null) return new HashSet<long>();

        Interlocked.Increment(ref _queryCount);
        _lastAccessed = DateTime.Now;

        if (_index.TryGetValue(value, out var idSet))
        {
            Interlocked.Increment(ref _hitCount);
            return new HashSet<long>(idSet);
        }

        return new HashSet<long>();
    }

    public abstract HashSet<long> GetRange(IComparable min, IComparable max);
    public abstract HashSet<long> GetByPattern(string pattern);

    public virtual void Clear()
    {
        _index.Clear();
        _queryCount = 0;
        _hitCount = 0;
    }

    public virtual IndexStatistics GetStatistics()
    {
        return new IndexStatistics
        {
            IndexType = GetType().Name,
            QueryCount = _queryCount,
            HitCount = _hitCount,
            IndexSize = _index.Count,
            UniqueValueCount = _index.Keys.Count,
            LastAccessed = _lastAccessed,
            CreatedTime = _createdTime,
            MemoryUsage = EstimateMemoryUsage()
        };
    }

    protected virtual long EstimateMemoryUsage()
    {
        // 粗略估计内存使用量
        long totalSize = 0;
        foreach (var kvp in _index)
        {
            totalSize += EstimateObjectSize(kvp.Key);
            totalSize += kvp.Value.Count * sizeof(long);
        }
        return totalSize;
    }

    protected virtual long EstimateObjectSize(object obj)
    {
        if (obj == null) return 0;
        
        return obj switch
        {
            string str => str.Length * sizeof(char),
            int => sizeof(int),
            long => sizeof(long),
            decimal => sizeof(decimal),
            double => sizeof(double),
            float => sizeof(float),
            DateTime => sizeof(long),
            bool => sizeof(bool),
            _ => 64 // 默认估算
        };
    }
}
