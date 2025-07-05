using System.Collections.Concurrent;
using System.Reflection;
using DH.Reflection;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 类型感知索引管理器
/// 根据属性类型自动选择合适的索引实现
/// </summary>
public static class TypedIndexManager
{
    // 类型感知索引存储：Type_PropertyName -> ITypedIndex
    private static readonly ConcurrentDictionary<string, ITypedIndex> _typedIndexes = new();
    
    // 索引使用统计
    private static readonly ConcurrentDictionary<string, IndexUsageStats> _usageStats = new();

    /// <summary>
    /// 获取或创建类型感知索引
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="propertyType">属性类型</param>
    /// <returns>类型感知索引</returns>
    public static ITypedIndex GetOrCreateIndex(Type type, string propertyName, Type propertyType)
    {
        var indexKey = GetIndexKey(type, propertyName);
        
        return _typedIndexes.GetOrAdd(indexKey, _ => CreateIndexForType(propertyType, type, propertyName));
    }

    /// <summary>
    /// 获取现有索引
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <returns>类型感知索引，如果不存在返回null</returns>
    public static ITypedIndex? GetIndex(Type type, string propertyName)
    {
        var indexKey = GetIndexKey(type, propertyName);
        _typedIndexes.TryGetValue(indexKey, out var index);
        return index;
    }

    /// <summary>
    /// 添加索引项
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="propertyType">属性类型</param>
    /// <param name="value">属性值</param>
    /// <param name="objectId">对象ID</param>
    public static void AddIndex(Type type, string propertyName, Type propertyType, object value, long objectId)
    {
        var index = GetOrCreateIndex(type, propertyName, propertyType);
        index.AddId(value, objectId);
        
        RecordUsage(type, propertyName, IndexOperation.Add);
    }

    /// <summary>
    /// 移除索引项
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="value">属性值</param>
    /// <param name="objectId">对象ID</param>
    public static void RemoveIndex(Type type, string propertyName, object value, long objectId)
    {
        var index = GetIndex(type, propertyName);
        if (index != null)
        {
            index.RemoveId(value, objectId);
            RecordUsage(type, propertyName, IndexOperation.Remove);
        }
    }

    /// <summary>
    /// 精确查询
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="value">查询值</param>
    /// <returns>匹配的对象ID集合</returns>
    public static HashSet<long> FindByValue(Type type, string propertyName, object value)
    {
        var index = GetIndex(type, propertyName);
        if (index != null)
        {
            RecordUsage(type, propertyName, IndexOperation.Query);
            return index.GetIds(value);
        }
        
        return new HashSet<long>();
    }

    /// <summary>
    /// 范围查询
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="min">最小值</param>
    /// <param name="max">最大值</param>
    /// <returns>匹配的对象ID集合</returns>
    public static HashSet<long> FindByRange(Type type, string propertyName, IComparable min, IComparable max)
    {
        var index = GetIndex(type, propertyName);
        if (index != null)
        {
            RecordUsage(type, propertyName, IndexOperation.RangeQuery);
            return index.GetRange(min, max);
        }
        
        return new HashSet<long>();
    }

    /// <summary>
    /// 模式匹配查询
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="pattern">匹配模式</param>
    /// <returns>匹配的对象ID集合</returns>
    public static HashSet<long> FindByPattern(Type type, string propertyName, string pattern)
    {
        var index = GetIndex(type, propertyName);
        if (index != null)
        {
            RecordUsage(type, propertyName, IndexOperation.PatternQuery);
            return index.GetByPattern(pattern);
        }
        
        return new HashSet<long>();
    }

    /// <summary>
    /// 清空指定类型的所有索引
    /// </summary>
    /// <param name="type">对象类型</param>
    public static void ClearIndexes(Type type)
    {
        var keysToRemove = _typedIndexes.Keys.Where(key => key.StartsWith(type.FullName + "_")).ToList();
        
        foreach (var key in keysToRemove)
        {
            if (_typedIndexes.TryRemove(key, out var index))
            {
                index.Clear();
            }
        }
    }

    /// <summary>
    /// 清空所有索引
    /// </summary>
    public static void ClearAllIndexes()
    {
        foreach (var index in _typedIndexes.Values)
        {
            index.Clear();
        }
        _typedIndexes.Clear();
        _usageStats.Clear();
    }

    /// <summary>
    /// 获取索引统计信息
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <returns>索引统计信息</returns>
    public static IndexStatistics? GetIndexStatistics(Type type, string propertyName)
    {
        var index = GetIndex(type, propertyName);
        if (index != null)
        {
            var stats = index.GetStatistics();
            stats.PropertyName = propertyName;
            return stats;
        }
        
        return null;
    }

    /// <summary>
    /// 获取所有索引统计信息
    /// </summary>
    /// <returns>所有索引的统计信息</returns>
    public static List<IndexStatistics> GetAllIndexStatistics()
    {
        var result = new List<IndexStatistics>();
        
        foreach (var kvp in _typedIndexes)
        {
            var parts = kvp.Key.Split(new char[] { '_' }, 2);
            if (parts.Length == 2)
            {
                var stats = kvp.Value.GetStatistics();
                stats.PropertyName = parts[1];
                result.Add(stats);
            }
        }
        
        return result;
    }

    /// <summary>
    /// 获取索引使用统计
    /// </summary>
    /// <returns>使用统计信息</returns>
    public static Dictionary<string, IndexUsageStats> GetUsageStatistics()
    {
        return new Dictionary<string, IndexUsageStats>(_usageStats);
    }

    /// <summary>
    /// 推荐索引优化建议
    /// </summary>
    /// <returns>优化建议列表</returns>
    public static List<IndexOptimizationRecommendation> GetOptimizationRecommendations()
    {
        var recommendations = new List<IndexOptimizationRecommendation>();
        
        foreach (var kvp in _usageStats)
        {
            var stats = kvp.Value;
            var parts = kvp.Key.Split(new char[] { '_' }, 2);
            if (parts.Length != 2) continue;
            
            var recommendation = new IndexOptimizationRecommendation
            {
                TypeName = parts[0],
                PropertyName = parts[1],
                QueryCount = stats.QueryCount,
                HitRate = stats.HitCount > 0 ? (double)stats.HitCount / stats.QueryCount : 0
            };
            
            // 分析并给出建议
            if (stats.QueryCount == 0)
            {
                recommendation.Recommendation = "考虑删除未使用的索引以节省内存";
                recommendation.Priority = RecommendationPriority.Low;
            }
            else if (recommendation.HitRate < 0.1)
            {
                recommendation.Recommendation = "索引命中率过低，考虑优化查询条件或调整索引策略";
                recommendation.Priority = RecommendationPriority.Medium;
            }
            else if (stats.QueryCount > 1000 && recommendation.HitRate > 0.8)
            {
                recommendation.Recommendation = "高频高效索引，表现良好";
                recommendation.Priority = RecommendationPriority.Info;
            }
            
            recommendations.Add(recommendation);
        }
        
        return recommendations.OrderByDescending(r => r.Priority).ToList();
    }

    private static string GetIndexKey(Type type, string propertyName)
    {
        return $"{type.FullName}_{propertyName}";
    }

    private static ITypedIndex CreateIndexForType(Type propertyType, Type objectType, string propertyName)
    {
        // 处理可空类型
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        
        return Type.GetTypeCode(underlyingType) switch
        {
            TypeCode.String => new StringIndex(),
            TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 or 
            TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 or
            TypeCode.Byte or TypeCode.SByte or 
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => new NumericIndex(),
            TypeCode.DateTime => new DateTimeIndex(),
            TypeCode.Boolean => new BooleanIndex(),
            _ when underlyingType == typeof(DateTimeOffset) => new DateTimeIndex(),
            _ when underlyingType == typeof(Guid) => new GenericIndex(),
            _ => new GenericIndex()
        };
    }

    private static void RecordUsage(Type type, string propertyName, IndexOperation operation)
    {
        var key = GetIndexKey(type, propertyName);
        _usageStats.AddOrUpdate(key,
            new IndexUsageStats { QueryCount = operation == IndexOperation.Query ? 1 : 0 },
            (k, existing) =>
            {
                switch (operation)
                {
                    case IndexOperation.Query:
                    case IndexOperation.RangeQuery:
                    case IndexOperation.PatternQuery:
                        existing.QueryCount++;
                        break;
                    case IndexOperation.Add:
                        existing.AddCount++;
                        break;
                    case IndexOperation.Remove:
                        existing.RemoveCount++;
                        break;
                }
                existing.LastAccessed = DateTime.Now;
                return existing;
            });
    }
}

/// <summary>
/// 索引操作类型
/// </summary>
public enum IndexOperation
{
    Query,
    RangeQuery,
    PatternQuery,
    Add,
    Remove
}

/// <summary>
/// 索引使用统计
/// </summary>
public class IndexUsageStats
{
    public int QueryCount { get; set; }
    public int HitCount { get; set; }
    public int AddCount { get; set; }
    public int RemoveCount { get; set; }
    public DateTime LastAccessed { get; set; } = DateTime.Now;
}

/// <summary>
/// 索引优化建议
/// </summary>
public class IndexOptimizationRecommendation
{
    public string TypeName { get; set; } = "";
    public string PropertyName { get; set; } = "";
    public int QueryCount { get; set; }
    public double HitRate { get; set; }
    public string Recommendation { get; set; } = "";
    public RecommendationPriority Priority { get; set; }
}

/// <summary>
/// 建议优先级
/// </summary>
public enum RecommendationPriority
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
