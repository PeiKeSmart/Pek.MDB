using System.Collections.Concurrent;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 数值类型索引 - 简化版，支持范围查询（无锁并发优化）
/// 优化说明：移除了复杂的读写锁机制，使用ConcurrentDictionary实现更好的并发性能
/// </summary>
public class NumericIndex : TypedIndexBase
{
    // 使用并发字典实现无锁操作，范围查询通过遍历实现
    private readonly ConcurrentDictionary<decimal, ConcurrentHashSet<long>> _numericIndex = new();

    public override void AddId(object value, long id)
    {
        if (value == null) return;

        var numericValue = ConvertToDecimal(value);
        if (numericValue == null) return;

        // 添加到基础索引
        base.AddId(numericValue, id);

        // 添加到数值索引 - 无锁操作
        _numericIndex.AddOrUpdate(numericValue.Value,
            new ConcurrentHashSet<long> { id },
            (key, existingSet) =>
            {
                existingSet.Add(id);
                return existingSet;
            });
    }

    public override void RemoveId(object value, long id)
    {
        if (value == null) return;

        var numericValue = ConvertToDecimal(value);
        if (numericValue == null) return;

        // 从基础索引移除
        base.RemoveId(numericValue, id);

        // 从数值索引移除 - 无锁操作
        if (_numericIndex.TryGetValue(numericValue.Value, out var idSet))
        {
            idSet.Remove(id);
            if (idSet.Count == 0)
            {
                _numericIndex.TryRemove(numericValue.Value, out _);
            }
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
        
        // 无锁范围查询 - 通过遍历实现
        foreach (var kvp in _numericIndex)
        {
            if (kvp.Key >= minValue && kvp.Key <= maxValue)
            {
                // 获取线程安全集合的快照
                foreach (var id in kvp.Value.ToHashSet())
                {
                    result.Add(id);
                }
            }
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
        _numericIndex.Clear();
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
        var numericSize = _numericIndex.Count * (sizeof(decimal) + sizeof(long) * 2); // 估算并发字典开销
        return baseSize + numericSize;
    }
}
