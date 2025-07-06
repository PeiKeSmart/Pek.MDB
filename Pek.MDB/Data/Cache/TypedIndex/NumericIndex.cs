using System.Collections.Concurrent;
using System.Globalization;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 数值类型索引 - 支持范围查询（线程安全优化版）
/// </summary>
public class NumericIndex : TypedIndexBase
{
    // 使用并发有序字典来支持范围查询 - 使用读写锁优化
    private readonly SortedDictionary<decimal, ConcurrentHashSet<long>> _sortedIndex = new();
    private readonly ReaderWriterLockSlim _sortedLock = new(LockRecursionPolicy.NoRecursion);

    public override void AddId(object value, long id)
    {
        if (value == null) return;

        var numericValue = ConvertToDecimal(value);
        if (numericValue == null) return;

        // 添加到基础索引
        base.AddId(numericValue, id);

        // 添加到有序索引 - 使用写锁
        _sortedLock.EnterWriteLock();
        try
        {
            if (!_sortedIndex.TryGetValue(numericValue.Value, out var idSet))
            {
                idSet = new ConcurrentHashSet<long>();
                _sortedIndex[numericValue.Value] = idSet;
            }
            idSet.Add(id);
        }
        finally
        {
            _sortedLock.ExitWriteLock();
        }
    }

    public override void RemoveId(object value, long id)
    {
        if (value == null) return;

        var numericValue = ConvertToDecimal(value);
        if (numericValue == null) return;

        // 从基础索引移除
        base.RemoveId(numericValue, id);

        // 从有序索引移除 - 使用写锁
        _sortedLock.EnterWriteLock();
        try
        {
            if (_sortedIndex.TryGetValue(numericValue.Value, out var idSet))
            {
                idSet.Remove(id);
                if (idSet.Count == 0)
                {
                    _sortedIndex.Remove(numericValue.Value);
                }
            }
        }
        finally
        {
            _sortedLock.ExitWriteLock();
        }
    }

    public override HashSet<long> GetRange(IComparable min, IComparable max)
    {
        var minValue = ConvertToDecimal(min);
        var maxValue = ConvertToDecimal(max);

        if (minValue == null || maxValue == null)
        {
            return new HashSet<long>();
        }

        Interlocked.Increment(ref _queryCount);
        _lastAccessed = DateTime.Now;

        var result = new HashSet<long>();
        
        // 使用读锁进行范围查询
        _sortedLock.EnterReadLock();
        try
        {
            var range = _sortedIndex.Where(kvp => kvp.Key >= minValue && kvp.Key <= maxValue);
            foreach (var kvp in range)
            {
                // 获取线程安全集合的快照
                foreach (var id in kvp.Value.ToHashSet())
                {
                    result.Add(id);
                }
            }
        }
        finally
        {
            _sortedLock.ExitReadLock();
        }

        if (result.Count > 0)
        {
            Interlocked.Increment(ref _hitCount);
        }

        return result;
    }

    public override HashSet<long> GetByPattern(string pattern)
    {
        // 数值索引不支持模式匹配
        return new HashSet<long>();
    }

    public override void Clear()
    {
        base.Clear();
        _sortedLock.EnterWriteLock();
        try
        {
            _sortedIndex.Clear();
        }
        finally
        {
            _sortedLock.ExitWriteLock();
        }
    }

    private decimal? ConvertToDecimal(object value)
    {
        if (value == null) return null;

        try
        {
            return value switch
            {
                decimal d => d,
                int i => i,
                long l => l,
                float f => (decimal)f,
                double db => (decimal)db,
                byte b => b,
                short s => s,
                string str when decimal.TryParse(str, out var d) => d,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    protected override long EstimateMemoryUsage()
    {
        var baseSize = base.EstimateMemoryUsage();
        var sortedSize = _sortedIndex.Count * (sizeof(decimal) + sizeof(long) * 2); // 估算有序字典额外开销
        return baseSize + sortedSize;
    }
}
