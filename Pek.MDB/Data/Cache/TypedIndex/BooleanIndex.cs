using System.Collections.Concurrent;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 布尔类型索引
/// </summary>
public class BooleanIndex : TypedIndexBase
{
    public override HashSet<long> GetRange(IComparable min, IComparable max)
    {
        // 布尔类型不支持范围查询
        return new HashSet<long>();
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
        
        // 支持多种布尔值表示
        var normalizedPattern = pattern.ToLowerInvariant();
        bool? targetValue = normalizedPattern switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => null
        };

        if (targetValue.HasValue)
        {
            if (_index.TryGetValue(targetValue.Value, out var idSet))
            {
                foreach (var id in idSet)
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

    public override void AddId(object value, long id)
    {
        var boolValue = ConvertToBoolean(value);
        if (boolValue.HasValue)
        {
            base.AddId(boolValue.Value, id);
        }
    }

    public override void RemoveId(object value, long id)
    {
        var boolValue = ConvertToBoolean(value);
        if (boolValue.HasValue)
        {
            base.RemoveId(boolValue.Value, id);
        }
    }

    private bool? ConvertToBoolean(object value)
    {
        if (value == null) return null;

        try
        {
            return value switch
            {
                bool b => b,
                string str => ConvertStringToBoolean(str),
                int i => i != 0,
                long l => l != 0,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private bool? ConvertStringToBoolean(string str)
    {
        if (string.IsNullOrEmpty(str)) return null;

        var normalized = str.ToLowerInvariant().Trim();
        return normalized switch
        {
            "true" or "1" or "yes" or "on" or "是" or "真" => true,
            "false" or "0" or "no" or "off" or "否" or "假" => false,
            _ => bool.TryParse(str, out var b) ? b : null
        };
    }
}
