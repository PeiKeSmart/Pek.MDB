using System.Collections.Concurrent;
using DH.Data.Cache.TypedIndex;
using DH.ORM;
using DH.Reflection;

namespace DH.Data.Cache;

/// <summary>
/// 统一索引管理器 - 自动提供最优索引性能
/// </summary>
public static class UnifiedIndexManager
{
    // 自动优化配置 - 默认启用类型感知索引以获得最优性能
    private static volatile bool _enableTypedIndex = true;
    private static readonly object _configLock = new();

    /// <summary>
    /// 启用或禁用类型感知索引（内部使用，通常保持默认）
    /// </summary>
    /// <param name="enable">是否启用</param>
    internal static void EnableTypedIndex(bool enable)
    {
        lock (_configLock)
        {
            _enableTypedIndex = enable;
        }
    }

    /// <summary>
    /// 检查是否启用了类型感知索引
    /// </summary>
    internal static bool IsTypedIndexEnabled()
    {
        return _enableTypedIndex;
    }

    /// <summary>
    /// 统一的添加索引接口（自动选择最优策略）
    /// </summary>
    public static void AddIndex(Type type, string propertyName, object value, long id)
    {
        // 自动使用最优索引策略
        if (_enableTypedIndex)
        {
            // 优先使用类型感知索引
            var propertyType = GetPropertyType(type, propertyName);
            if (propertyType != null)
            {
                TypedIndexManager.AddIndex(type, propertyName, propertyType, value, id);
                return;
            }
        }
        
        // 回退到传统字符串索引（确保兼容性）
        AddLegacyIndex(type, propertyName, value, id);
    }

    /// <summary>
    /// 统一的查询接口（自动选择最优索引）
    /// </summary>
    public static HashSet<long> FindIds(Type type, string propertyName, object value)
    {
        // 查询优化：自动选择最佳策略
        var strategy = QueryOptimizer.AnalyzeQuery(type, propertyName, QueryType.Exact, value);
        
        if (strategy.UseTypedIndex && _enableTypedIndex)
        {
            // 优先使用类型感知索引
            var typedResults = TypedIndexManager.FindByValue(type, propertyName, value);
            if (typedResults.Count > 0)
            {
                return typedResults;
            }
        }

        // 回退到传统索引
        return FindLegacyIds(type, propertyName, value);
    }

    /// <summary>
    /// 传统索引添加方法（保持原有逻辑不变）
    /// </summary>
    private static void AddLegacyIndex(Type type, string propertyName, object value, long id)
    {
        if (value == null || string.IsNullOrEmpty(value.ToString())) return;

        var propertyKey = GetPropertyKey(type.FullName!, propertyName);
        var valueKey = GetValueKey(propertyKey, value.ToString()!);

        // 直接调用 MemoryDB 的公开方法
        MemoryDB.AddIndexItem(valueKey, id);
    }

    /// <summary>
    /// 传统索引查询方法
    /// </summary>
    private static HashSet<long> FindLegacyIds(Type type, string propertyName, object value)
    {
        if (value == null) return new HashSet<long>();

        var propertyKey = GetPropertyKey(type.FullName!, propertyName);
        var valueKey = GetValueKey(propertyKey, value.ToString()!);

        return MemoryDB.GetIndexItems(valueKey);
    }

    /// <summary>
    /// 批量迁移现有索引到新系统（内部维护功能）
    /// </summary>
    internal static void MigrateLegacyIndexes()
    {
        if (!_enableTypedIndex) return;

        var indexList = MemoryDB.GetIndexListSnapshot();
        var migratedCount = 0;

        foreach (var kvp in indexList)
        {
            try
            {
                // 解析传统索引键格式：TypeName_PropertyName_Value
                var parts = kvp.Key.Split('_');
                if (parts.Length >= 3)
                {
                    var typeName = parts[0];
                    var propertyName = parts[1];
                    var value = string.Join("_", parts.Skip(2));

                    var type = Type.GetType(typeName);
                    if (type != null)
                    {
                        var propertyType = GetPropertyType(type, propertyName);
                        if (propertyType != null)
                        {
                            // 尝试转换值到正确类型
                            var convertedValue = ConvertValue(value, propertyType);
                            if (convertedValue != null)
                            {
                                foreach (var id in kvp.Value)
                                {
                                    TypedIndexManager.AddIndex(type, propertyName, propertyType, convertedValue, id);
                                }
                                migratedCount++;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 静默处理迁移失败的索引，不影响系统稳定性
            }
        }

        // 静默完成迁移，不输出控制台信息
    }

    /// <summary>
    /// 智能类型转换
    /// </summary>
    private static object? ConvertValue(string value, Type targetType)
    {
        try
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(int)) return int.Parse(value);
            if (targetType == typeof(long)) return long.Parse(value);
            if (targetType == typeof(double)) return double.Parse(value);
            if (targetType == typeof(DateTime)) return DateTime.Parse(value);
            if (targetType == typeof(bool)) return bool.Parse(value);
            
            // 其他类型使用通用转换
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取属性类型
    /// </summary>
    private static Type? GetPropertyType(Type type, string propertyName)
    {
        return type.GetProperty(propertyName)?.PropertyType;
    }

    /// <summary>
    /// 获取属性键（兼容原有格式）
    /// </summary>
    private static string GetPropertyKey(string typeName, string propertyName)
    {
        return $"{typeName}_{propertyName}";
    }

    /// <summary>
    /// 获取值键（兼容原有格式）
    /// </summary>
    private static string GetValueKey(string propertyKey, string value)
    {
        return $"{propertyKey}_{value}";
    }
}