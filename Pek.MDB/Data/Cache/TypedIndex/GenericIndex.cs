namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 通用类型索引 - 适用于其他不特殊处理的类型
/// </summary>
public class GenericIndex : TypedIndexBase
{
    public override HashSet<long> GetRange(IComparable min, IComparable max)
    {
        if (min == null || max == null)
        {
            return new HashSet<long>();
        }

        Interlocked.Increment(ref _queryCount);
        _lastAccessed = DateTime.Now;

        var result = new HashSet<long>();
        
        // 对于通用类型，需要遍历所有值进行比较
        foreach (var kvp in _index)
        {
            if (kvp.Key is IComparable comparable)
            {
                try
                {
                    if (comparable.CompareTo(min) >= 0 && comparable.CompareTo(max) <= 0)
                    {
                        // 获取线程安全集合的快照
                        foreach (var id in kvp.Value.ToHashSet())
                        {
                            result.Add(id);
                        }
                    }
                }
                catch
                {
                    // 如果比较失败，跳过这个值
                    continue;
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
        if (string.IsNullOrEmpty(pattern))
        {
            return new HashSet<long>();
        }

        Interlocked.Increment(ref _queryCount);
        _lastAccessed = DateTime.Now;

        var result = new HashSet<long>();
        
        // 对于通用类型，基于字符串表示进行模式匹配
        foreach (var kvp in _index)
        {
            var stringValue = kvp.Key?.ToString();
            if (!string.IsNullOrEmpty(stringValue))
            {
                if (IsPatternMatch(stringValue, pattern))
                {
                    // 获取线程安全集合的快照
                    foreach (var id in kvp.Value.ToHashSet())
                    {
                        result.Add(id);
                    }
                }
            }
        }

        if (result.Count > 0)
        {
            Interlocked.Increment(ref _hitCount);
        }

        return result;
    }

    private bool IsPatternMatch(string value, string pattern)
    {
        try
        {
            // 简单的模式匹配：支持 * 通配符
            if (pattern == "*") return true;
            
            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                // *abc* - 包含匹配
                var searchTerm = pattern.Substring(1, pattern.Length - 2);
                return value.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            else if (pattern.StartsWith("*"))
            {
                // *abc - 后缀匹配
                var suffix = pattern.Substring(1);
                return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }
            else if (pattern.EndsWith("*"))
            {
                // abc* - 前缀匹配
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                // 精确匹配（忽略大小写）
                return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return false;
        }
    }
}
