using System.Collections.Concurrent;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 字符串类型索引 - 简化版，支持精确匹配和基础模糊查询
/// 优化说明：移除了复杂的前缀/后缀预建索引，大幅减少内存使用和提升写入性能
/// </summary>
public class StringIndex : TypedIndexBase
{
    // 只保留两个索引：精确匹配和大小写不敏感匹配
    private readonly ConcurrentDictionary<string, ConcurrentHashSet<long>> _lowerCaseIndex = new();

    /// <summary>
    /// 添加辅助方法：向指定索引添加ID
    /// </summary>
    private void AddToIndex(ConcurrentDictionary<string, ConcurrentHashSet<long>> index, string key, long id)
    {
        index.AddOrUpdate(key,
            new ConcurrentHashSet<long> { id },
            (_, existingSet) =>
            {
                existingSet.Add(id);
                return existingSet;
            });
    }

    public override void AddId(object value, long id)
    {
        if (value == null) return;

        var stringValue = value.ToString();
        if (string.IsNullOrEmpty(stringValue)) return;

        // 添加到基础索引（继承自 TypedIndexBase）
        base.AddId(stringValue, id);

        // 添加到大小写不敏感索引
        var lowerValue = stringValue.ToLowerInvariant();
        AddToIndex(_lowerCaseIndex, lowerValue, id);
    }

    public override void RemoveId(object value, long id)
    {
        if (value == null) return;

        var stringValue = value.ToString();
        if (string.IsNullOrEmpty(stringValue)) return;

        // 从基础索引移除
        base.RemoveId(stringValue, id);

        // 从大小写不敏感索引移除
        var lowerValue = stringValue.ToLowerInvariant();
        if (_lowerCaseIndex.TryGetValue(lowerValue, out var lowerSet))
        {
            lowerSet.Remove(id);
            if (lowerSet.Count == 0)
            {
                _lowerCaseIndex.TryRemove(lowerValue, out _);
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
        
        // 使用大小写不敏感索引进行范围查询
        var minLower = minStr.ToLowerInvariant();
        var maxLower = maxStr.ToLowerInvariant();
        
        foreach (var kvp in _lowerCaseIndex)
        {
            var key = kvp.Key;
            if (!string.IsNullOrEmpty(key) && 
                string.Compare(key, minLower, StringComparison.OrdinalIgnoreCase) >= 0 &&
                string.Compare(key, maxLower, StringComparison.OrdinalIgnoreCase) <= 0)
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

        if (!pattern.Contains("*"))
        {
            // 精确匹配（大小写不敏感）
            if (_lowerCaseIndex.TryGetValue(lowerPattern, out var exactSet))
            {
                foreach (var id in exactSet)
                {
                    result.Add(id);
                }
            }
        }
        else
        {
            // 模糊匹配：通过遍历实现（简单但有效）
            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                // *abc* - 包含匹配
                var searchTerm = lowerPattern.Trim('*');
                foreach (var kvp in _lowerCaseIndex)
                {
                    if (kvp.Key.Contains(searchTerm))
                    {
                        foreach (var id in kvp.Value)
                        {
                            result.Add(id);
                        }
                    }
                }
            }
            else if (pattern.EndsWith("*"))
            {
                // abc* - 前缀匹配
                var prefix = lowerPattern.TrimEnd('*');
                foreach (var kvp in _lowerCaseIndex)
                {
                    if (kvp.Key.StartsWith(prefix))
                    {
                        foreach (var id in kvp.Value)
                        {
                            result.Add(id);
                        }
                    }
                }
            }
            else if (pattern.StartsWith("*"))
            {
                // *abc - 后缀匹配
                var suffix = lowerPattern.TrimStart('*');
                foreach (var kvp in _lowerCaseIndex)
                {
                    if (kvp.Key.EndsWith(suffix))
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
                // 复杂通配符：简单实现，不使用正则表达式
                foreach (var kvp in _lowerCaseIndex)
                {
                    if (IsWildcardMatch(kvp.Key, lowerPattern))
                    {
                        foreach (var id in kvp.Value)
                        {
                            result.Add(id);
                        }
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

    /// <summary>
    /// 简单的通配符匹配实现（避免使用正则表达式）
    /// </summary>
    private bool IsWildcardMatch(string text, string pattern)
    {
        var textIndex = 0;
        var patternIndex = 0;

        while (textIndex < text.Length && patternIndex < pattern.Length)
        {
            if (pattern[patternIndex] == '*')
            {
                patternIndex++;
                if (patternIndex == pattern.Length)
                    return true; // 模式以*结尾，匹配剩余所有字符

                // 寻找*后面的字符在文本中的位置
                var nextChar = pattern[patternIndex];
                while (textIndex < text.Length && text[textIndex] != nextChar)
                {
                    textIndex++;
                }
            }
            else if (text[textIndex] == pattern[patternIndex])
            {
                textIndex++;
                patternIndex++;
            }
            else
            {
                return false;
            }
        }

        // 检查是否完全匹配
        return patternIndex == pattern.Length || 
               (patternIndex == pattern.Length - 1 && pattern[patternIndex] == '*');
    }

    public override void Clear()
    {
        base.Clear();
        _lowerCaseIndex.Clear();
    }

    protected override long EstimateMemoryUsage()
    {
        var baseSize = base.EstimateMemoryUsage();
        var lowerCaseSize = _lowerCaseIndex.Sum(kvp => 
            kvp.Key.Length * sizeof(char) + kvp.Value.Count * sizeof(long));
        
        return baseSize + lowerCaseSize;
    }
}
