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
    /// 清除所有索引
    /// </summary>
    public static void ClearAllIndexes()
    {
        _typedIndexes.Clear();
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
    /// 获取索引总数
    /// </summary>
    /// <returns>索引总数</returns>
    public static int GetIndexCount()
    {
        return _typedIndexes.Count;
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
}
