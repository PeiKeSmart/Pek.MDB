using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 字符串类型索引 - 支持模糊查询
/// </summary>
public class StringIndex : TypedIndexBase
{
    // 用于模糊查询的额外索引
    private readonly ConcurrentDictionary<string, ConcurrentHashSet<long>> _lowerCaseIndex = new();
    private readonly ConcurrentDictionary<string, ConcurrentHashSet<long>> _prefixIndex = new();
    private readonly ConcurrentDictionary<string, ConcurrentHashSet<long>> _suffixIndex = new();

    public override void AddId(object value, long id)
    {
        if (value == null) return;

        var stringValue = value.ToString();
        if (string.IsNullOrEmpty(stringValue)) return;

        // 添加到基础索引
        base.AddId(stringValue, id);

        // 添加到模糊查询索引
        var lowerValue = stringValue.ToLowerInvariant();
        _lowerCaseIndex.AddOrUpdate(lowerValue,
            new ConcurrentHashSet<long> { id },
            (key, existingSet) =>
            {
                existingSet.Add(id);
                return existingSet;
            });

        // 添加前缀索引（支持以xxx开头的查询）
        for (var i = 1; i <= Math.Min(stringValue.Length, 10); i++)
        {
            var prefix = stringValue.Substring(0, i).ToLowerInvariant();
            _prefixIndex.AddOrUpdate(prefix,
                new ConcurrentHashSet<long> { id },
                (key, existingSet) =>
                {
                    existingSet.Add(id);
                    return existingSet;
                });
        }

        // 添加后缀索引（支持以xxx结尾的查询）
        for (var i = 1; i <= Math.Min(stringValue.Length, 10); i++)
        {
            var suffix = stringValue.Substring(stringValue.Length - i).ToLowerInvariant();
            _suffixIndex.AddOrUpdate(suffix,
                new ConcurrentHashSet<long> { id },
                (key, existingSet) =>
                {
                    existingSet.Add(id);
                    return existingSet;
                });
        }
    }

    public override void RemoveId(object value, long id)
    {
        if (value == null) return;

        var stringValue = value.ToString();
        if (string.IsNullOrEmpty(stringValue)) return;

        // 从基础索引移除
        base.RemoveId(stringValue, id);

        // 从模糊查询索引移除
        var lowerValue = stringValue.ToLowerInvariant();
        if (_lowerCaseIndex.TryGetValue(lowerValue, out var lowerSet))
        {
            lowerSet.Remove(id);
            if (lowerSet.Count == 0)
            {
                _lowerCaseIndex.TryRemove(lowerValue, out _);
            }
        }

        // 从前缀索引移除
        for (var i = 1; i <= Math.Min(stringValue.Length, 10); i++)
        {
            var prefix = stringValue.Substring(0, i).ToLowerInvariant();
            if (_prefixIndex.TryGetValue(prefix, out var prefixSet))
            {
                prefixSet.Remove(id);
                if (prefixSet.Count == 0)
                {
                    _prefixIndex.TryRemove(prefix, out _);
                }
            }
        }

        // 从后缀索引移除
        for (var i = 1; i <= Math.Min(stringValue.Length, 10); i++)
        {
            var suffix = stringValue.Substring(stringValue.Length - i).ToLowerInvariant();
            if (_suffixIndex.TryGetValue(suffix, out var suffixSet))
            {
                suffixSet.Remove(id);
                if (suffixSet.Count == 0)
                {
                    _suffixIndex.TryRemove(suffix, out _);
                }
            }
        }
    }

    public override HashSet<long> GetRange(IComparable min, IComparable max)
    {
        var minStr = min?.ToString();
        var maxStr = max?.ToString();

        if (string.IsNullOrEmpty(minStr) || string.IsNullOrEmpty(maxStr))
        {
            return new HashSet<long>();
        }

        Interlocked.Increment(ref _queryCount);
        _lastAccessed = DateTime.Now;

        var result = new HashSet<long>();
        foreach (var kvp in _index)
        {
            var key = kvp.Key?.ToString();
            if (!string.IsNullOrEmpty(key) && 
                string.Compare(key, minStr, StringComparison.OrdinalIgnoreCase) >= 0 &&
                string.Compare(key, maxStr, StringComparison.OrdinalIgnoreCase) <= 0)
            {
                foreach (var id in kvp.Value)
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
        if (string.IsNullOrEmpty(pattern))
        {
            return new HashSet<long>();
        }

        Interlocked.Increment(ref _queryCount);
        _lastAccessed = DateTime.Now;

        var result = new HashSet<long>();
        var lowerPattern = pattern.ToLowerInvariant();

        // 处理不同的模式
        if (lowerPattern.EndsWith("*"))
        {
            // 前缀匹配：abc* 匹配以abc开头的
            var prefix = lowerPattern.Substring(0, lowerPattern.Length - 1);
            if (_prefixIndex.TryGetValue(prefix, out var prefixSet))
            {
                foreach (var id in prefixSet)
                {
                    result.Add(id);
                }
            }
        }
        else if (lowerPattern.StartsWith("*"))
        {
            // 后缀匹配：*abc 匹配以abc结尾的
            var suffix = lowerPattern.Substring(1);
            if (_suffixIndex.TryGetValue(suffix, out var suffixSet))
            {
                foreach (var id in suffixSet)
                {
                    result.Add(id);
                }
            }
        }
        else if (lowerPattern.Contains("*"))
        {
            // 通配符匹配：使用正则表达式
            var regexPattern = "^" + Regex.Escape(lowerPattern).Replace("\\*", ".*") + "$";
            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
            
            foreach (var kvp in _lowerCaseIndex)
            {
                if (regex.IsMatch(kvp.Key))
                {
                    foreach (var id in kvp.Value)
                    {
                        result.Add(id);
                    }
                }
            }
        }
        else
        {
            // 包含匹配：查找包含指定字符串的
            foreach (var kvp in _lowerCaseIndex)
            {
                if (kvp.Key.Contains(lowerPattern))
                {
                    foreach (var id in kvp.Value)
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

    public override void Clear()
    {
        base.Clear();
        _lowerCaseIndex.Clear();
        _prefixIndex.Clear();
        _suffixIndex.Clear();
    }

    protected override long EstimateMemoryUsage()
    {
        var baseSize = base.EstimateMemoryUsage();
        var lowerCaseSize = _lowerCaseIndex.Sum(kvp => kvp.Key.Length * sizeof(char) + kvp.Value.Count * sizeof(long));
        var prefixSize = _prefixIndex.Sum(kvp => kvp.Key.Length * sizeof(char) + kvp.Value.Count * sizeof(long));
        var suffixSize = _suffixIndex.Sum(kvp => kvp.Key.Length * sizeof(char) + kvp.Value.Count * sizeof(long));
        
        return baseSize + lowerCaseSize + prefixSize + suffixSize;
    }
}
