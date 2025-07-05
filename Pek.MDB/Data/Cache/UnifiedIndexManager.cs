using System.Collections.Concurrent;
using DH.Data.Cache.TypedIndex;
using DH.ORM;
using DH.Reflection;

namespace DH.Data.Cache;

/// <summary>
/// 统一索引管理器 - 提供向后兼容的渐进式重构
/// </summary>
public static class UnifiedIndexManager
{
    // 兼容性模式配置
    private static volatile bool _legacyMode = true;
    private static volatile bool _dualMode = false; // 双重索引模式
    private static readonly object _configLock = new();

    /// <summary>
    /// 设置索引模式
    /// </summary>
    /// <param name="mode">索引模式</param>
    public static void SetIndexMode(IndexMode mode)
    {
        lock (_configLock)
        {
            switch (mode)
            {
                case IndexMode.Legacy:
                    _legacyMode = true;
                    _dualMode = false;
                    break;
                case IndexMode.Dual:
                    _legacyMode = true;
                    _dualMode = true;
                    break;
                case IndexMode.Modern:
                    _legacyMode = false;
                    _dualMode = false;
                    break;
            }
        }
    }

    /// <summary>
    /// 获取当前索引模式
    /// </summary>
    public static IndexMode GetCurrentMode()
    {
        if (_legacyMode && _dualMode) return IndexMode.Dual;
        if (_legacyMode) return IndexMode.Legacy;
        return IndexMode.Modern;
    }

    /// <summary>
    /// 统一的添加索引接口（兼容所有模式）
    /// </summary>
    public static void AddIndex(Type type, string propertyName, object value, long id)
    {
        if (_legacyMode)
        {
            // 使用传统字符串索引（确保100%兼容）
            AddLegacyIndex(type, propertyName, value, id);
        }

        if (_dualMode || !_legacyMode)
        {
            // 使用类型感知索引
            var propertyType = GetPropertyType(type, propertyName);
            if (propertyType != null)
            {
                TypedIndexManager.AddIndex(type, propertyName, propertyType, value, id);
            }
        }
    }

    /// <summary>
    /// 统一的查询接口（自动选择最优索引）
    /// </summary>
    public static HashSet<long> FindIds(Type type, string propertyName, object value)
    {
        var results = new HashSet<long>();
        
        // 查询优化：根据当前模式选择最佳策略
        var strategy = QueryOptimizer.AnalyzeQuery(type, propertyName, QueryType.Exact, value);
        
        if (strategy.UseTypedIndex && (_dualMode || !_legacyMode))
        {
            // 优先使用类型感知索引
            var typedResults = TypedIndexManager.FindByValue(type, propertyName, value);
            if (typedResults.Count > 0)
            {
                return typedResults;
            }
        }

        if (_legacyMode)
        {
            // 回退到传统索引
            return FindLegacyIds(type, propertyName, value);
        }

        return results;
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
    /// 批量迁移现有索引到新系统
    /// </summary>
    public static void MigrateLegacyIndexes()
    {
        if (GetCurrentMode() == IndexMode.Legacy) return;

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
            catch (Exception ex)
            {
                // 记录迁移失败的索引，但不中断整个过程
                Console.WriteLine($"Failed to migrate index {kvp.Key}: {ex.Message}");
            }
        }

        Console.WriteLine($"Successfully migrated {migratedCount} legacy indexes to typed indexes");
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

    /// <summary>
    /// 性能对比分析
    /// </summary>
    public static IndexPerformanceReport ComparePerformance(Type type, string propertyName, object value, int iterations = 1000)
    {
        var report = new IndexPerformanceReport
        {
            Type = type.Name,
            PropertyName = propertyName,
            Iterations = iterations
        };

        // 测试传统索引性能
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var _ = FindLegacyIds(type, propertyName, value);
        }
        sw.Stop();
        report.LegacyIndexTime = sw.ElapsedMilliseconds;

        // 测试类型感知索引性能
        sw.Restart();
        for (var i = 0; i < iterations; i++)
        {
            var _ = TypedIndexManager.FindByValue(type, propertyName, value);
        }
        sw.Stop();
        report.TypedIndexTime = sw.ElapsedMilliseconds;

        report.PerformanceGain = report.LegacyIndexTime > 0 
            ? (double)report.LegacyIndexTime / report.TypedIndexTime 
            : 0;

        return report;
    }
}

/// <summary>
/// 索引模式枚举
/// </summary>
public enum IndexMode
{
    /// <summary>
    /// 传统模式：只使用字符串索引（完全向后兼容）
    /// </summary>
    Legacy,
    
    /// <summary>
    /// 双重模式：同时维护两套索引（过渡期使用）
    /// </summary>
    Dual,
    
    /// <summary>
    /// 现代模式：只使用类型感知索引（最优性能）
    /// </summary>
    Modern
}

/// <summary>
/// 索引性能报告
/// </summary>
public class IndexPerformanceReport
{
    public string Type { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public int Iterations { get; set; }
    public long LegacyIndexTime { get; set; }
    public long TypedIndexTime { get; set; }
    public double PerformanceGain { get; set; }
    
    public string GetSummary()
    {
        return $"Type: {Type}.{PropertyName}, Iterations: {Iterations}, " +
               $"Legacy: {LegacyIndexTime}ms, Typed: {TypedIndexTime}ms, " +
               $"Gain: {PerformanceGain:F2}x";
    }
}
