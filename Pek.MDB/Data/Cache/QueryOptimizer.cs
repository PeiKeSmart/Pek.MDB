using System.Collections.Concurrent;
using System.Text;
using DH.Data.Cache.TypedIndex;

namespace DH.Data.Cache;

/// <summary>
/// 查询优化器 - 自动选择最优查询策略
/// </summary>
public static class QueryOptimizer
{
    // 查询统计缓存
    private static readonly ConcurrentDictionary<string, QueryStats> _queryStats = new();
    
    // 查询策略缓存
    private static readonly ConcurrentDictionary<string, QueryStrategy> _strategies = new();
    
    // 配置参数
    private static volatile bool _enableOptimization = true;
    private static volatile int _minQueryCountForOptimization = 10;
    private static double _typeAwareBenchmarkThreshold = 1.5; // 类型感知索引需要比传统索引快1.5倍才切换
    private static readonly object _thresholdLock = new object();

    /// <summary>
    /// 启用或禁用查询优化
    /// </summary>
    /// <param name="enable">是否启用</param>
    public static void EnableOptimization(bool enable)
    {
        _enableOptimization = enable;
    }

    /// <summary>
    /// 设置最小查询次数阈值（达到此阈值后才进行优化分析）
    /// </summary>
    /// <param name="minCount">最小查询次数</param>
    public static void SetMinQueryCountForOptimization(int minCount)
    {
        _minQueryCountForOptimization = minCount;
    }

    /// <summary>
    /// 分析查询并返回最优策略
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="queryType">查询类型</param>
    /// <param name="value">查询值</param>
    /// <returns>推荐的查询策略</returns>
    public static QueryStrategy AnalyzeQuery(Type type, string propertyName, QueryType queryType, object? value = null)
    {
        if (!_enableOptimization)
        {
            return new QueryStrategy
            {
                UseTypedIndex = MemoryDB.IsTypedIndexEnabled(),
                EstimatedPerformance = QueryPerformance.Medium,
                Reason = "优化器已禁用，使用默认策略"
            };
        }

        var queryKey = GetQueryKey(type, propertyName, queryType);
        
        // 获取或创建查询统计
        var stats = _queryStats.GetOrAdd(queryKey, _ => new QueryStats());
        
        // 如果查询次数不足，返回默认策略并收集统计
        if (stats.TotalQueries < _minQueryCountForOptimization)
        {
            stats.TotalQueries++;
            return new QueryStrategy
            {
                UseTypedIndex = MemoryDB.IsTypedIndexEnabled(),
                EstimatedPerformance = QueryPerformance.Medium,
                Reason = $"数据收集中 ({stats.TotalQueries}/{_minQueryCountForOptimization})"
            };
        }

        // 获取缓存的策略
        if (_strategies.TryGetValue(queryKey, out var cachedStrategy))
        {
            stats.TotalQueries++;
            return cachedStrategy;
        }

        // 分析并生成新策略
        var strategy = GenerateStrategy(type, propertyName, queryType, value, stats);
        _strategies[queryKey] = strategy;
        stats.TotalQueries++;
        
        return strategy;
    }

    /// <summary>
    /// 记录查询执行结果
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="queryType">查询类型</param>
    /// <param name="useTypedIndex">是否使用了类型感知索引</param>
    /// <param name="executionTime">执行时间（毫秒）</param>
    /// <param name="resultCount">结果数量</param>
    public static void RecordQueryResult(Type type, string propertyName, QueryType queryType, 
        bool useTypedIndex, double executionTime, int resultCount)
    {
        if (!_enableOptimization) return;

        var queryKey = GetQueryKey(type, propertyName, queryType);
        var stats = _queryStats.GetOrAdd(queryKey, _ => new QueryStats());

        lock (stats)
        {
            if (useTypedIndex)
            {
                stats.TypedIndexQueries++;
                stats.TypedIndexTotalTime += executionTime;
                if (stats.TypedIndexMinTime == 0 || executionTime < stats.TypedIndexMinTime)
                    stats.TypedIndexMinTime = executionTime;
                if (executionTime > stats.TypedIndexMaxTime)
                    stats.TypedIndexMaxTime = executionTime;
            }
            else
            {
                stats.TraditionalQueries++;
                stats.TraditionalTotalTime += executionTime;
                if (stats.TraditionalMinTime == 0 || executionTime < stats.TraditionalMinTime)
                    stats.TraditionalMinTime = executionTime;
                if (executionTime > stats.TraditionalMaxTime)
                    stats.TraditionalMaxTime = executionTime;
            }

            stats.LastResultCount = resultCount;
            stats.LastUpdated = DateTime.Now;
        }

        // 如果收集到足够数据，重新分析策略
        if (stats.TotalQueries > _minQueryCountForOptimization && 
            stats.TotalQueries % 50 == 0) // 每50次查询重新评估一次
        {
            _strategies.TryRemove(queryKey, out _); // 清除旧策略，强制重新分析
        }
    }

    /// <summary>
    /// 获取查询统计信息
    /// </summary>
    /// <returns>查询统计信息</returns>
    public static Dictionary<string, QueryStats> GetQueryStatistics()
    {
        return new Dictionary<string, QueryStats>(_queryStats);
    }

    /// <summary>
    /// 生成优化报告
    /// </summary>
    /// <returns>优化报告</returns>
    public static string GenerateOptimizationReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 查询优化器报告");
        sb.AppendLine($"## 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("## 优化器配置");
        sb.AppendLine($"- 优化器状态: {(_enableOptimization ? "启用" : "禁用")}");
        sb.AppendLine($"- 最小查询次数阈值: {_minQueryCountForOptimization}");
        sb.AppendLine($"- 类型感知索引性能阈值: {_typeAwareBenchmarkThreshold:F1}x");
        sb.AppendLine();

        var stats = _queryStats.ToList();
        sb.AppendLine($"## 总体统计");
        sb.AppendLine($"- 跟踪的查询模式: {stats.Count}");
        sb.AppendLine($"- 缓存的优化策略: {_strategies.Count}");
        sb.AppendLine();

        if (stats.Any())
        {
            sb.AppendLine("## 查询性能分析");
            
            var performanceData = stats
                .Where(s => s.Value.HasBothIndexData)
                .Select(s => new
                {
                    Query = s.Key,
                    Stats = s.Value,
                    TypedAvg = s.Value.TypedIndexAvgTime,
                    TraditionalAvg = s.Value.TraditionalAvgTime,
                    Improvement = s.Value.TraditionalAvgTime > 0 ? 
                        s.Value.TraditionalAvgTime / s.Value.TypedIndexAvgTime : 0
                })
                .OrderByDescending(x => x.Improvement)
                .ToList();

            if (performanceData.Any())
            {
                sb.AppendLine("### 类型感知索引性能提升 (Top 10)");
                foreach (var data in performanceData.Take(10))
                {
                    sb.AppendLine($"- {data.Query}: {data.Improvement:F2}x 提升 " +
                        $"({data.TraditionalAvg:F2}ms → {data.TypedAvg:F2}ms)");
                }
                sb.AppendLine();
            }

            // 策略分布统计
            var strategies = _strategies.Values.ToList();
            var typedIndexCount = strategies.Count(s => s.UseTypedIndex);
            var traditionalCount = strategies.Count - typedIndexCount;

            sb.AppendLine("## 策略分布");
            sb.AppendLine($"- 类型感知索引策略: {typedIndexCount} ({(double)typedIndexCount / strategies.Count:P})");
            sb.AppendLine($"- 传统索引策略: {traditionalCount} ({(double)traditionalCount / strategies.Count:P})");
            sb.AppendLine();

            // 性能等级分布
            var performanceLevels = strategies.GroupBy(s => s.EstimatedPerformance)
                .ToDictionary(g => g.Key, g => g.Count());

            sb.AppendLine("## 性能等级分布");
            foreach (QueryPerformance level in Enum.GetValues(typeof(QueryPerformance)))
            {
                var count = performanceLevels.ContainsKey(level) ? performanceLevels[level] : 0;
                sb.AppendLine($"- {level}: {count} 个策略");
            }
        }
        else
        {
            sb.AppendLine("暂无查询统计数据");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 清理过期的统计数据
    /// </summary>
    /// <param name="maxAge">最大保留时间</param>
    public static void CleanupOldStatistics(TimeSpan maxAge)
    {
        var cutoffTime = DateTime.Now - maxAge;
        var keysToRemove = _queryStats
            .Where(kvp => kvp.Value.LastUpdated < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _queryStats.TryRemove(key, out _);
            _strategies.TryRemove(key, out _);
        }
    }

    private static QueryStrategy GenerateStrategy(Type type, string propertyName, QueryType queryType, 
        object? value, QueryStats stats)
    {
        var strategy = new QueryStrategy();

        // 分析查询类型适合性
        switch (queryType)
        {
            case QueryType.Exact:
                strategy.EstimatedPerformance = QueryPerformance.High;
                strategy.UseTypedIndex = ShouldUseTypedIndex(stats, "精确查询适合类型感知索引");
                break;

            case QueryType.Range:
                // 范围查询更适合类型感知索引（特别是数值和日期类型）
                strategy.EstimatedPerformance = QueryPerformance.High;
                strategy.UseTypedIndex = true;
                strategy.Reason = "范围查询优先使用类型感知索引";
                break;

            case QueryType.Pattern:
                // 模式匹配查询，字符串类型索引更有优势
                var propertyType = type.GetProperty(propertyName)?.PropertyType;
                if (propertyType == typeof(string))
                {
                    strategy.EstimatedPerformance = QueryPerformance.Medium;
                    strategy.UseTypedIndex = true;
                    strategy.Reason = "字符串模式匹配使用类型感知索引";
                }
                else
                {
                    strategy.EstimatedPerformance = QueryPerformance.Low;
                    strategy.UseTypedIndex = false;
                    strategy.Reason = "非字符串类型的模式匹配使用传统索引";
                }
                break;

            case QueryType.Multiple:
                strategy.EstimatedPerformance = QueryPerformance.Medium;
                strategy.UseTypedIndex = ShouldUseTypedIndex(stats, "复合查询根据性能数据选择");
                break;

            default:
                strategy.EstimatedPerformance = QueryPerformance.Medium;
                strategy.UseTypedIndex = MemoryDB.IsTypedIndexEnabled();
                strategy.Reason = "默认策略";
                break;
        }

        return strategy;
    }

    private static bool ShouldUseTypedIndex(QueryStats stats, string baseReason)
    {
        // 如果没有性能对比数据，使用全局设置
        if (!stats.HasBothIndexData)
        {
            return MemoryDB.IsTypedIndexEnabled();
        }

        // 如果类型感知索引平均性能明显更好，推荐使用
        var improvement = stats.TraditionalAvgTime / stats.TypedIndexAvgTime;
        return improvement >= _typeAwareBenchmarkThreshold;
    }

    private static string GetQueryKey(Type type, string propertyName, QueryType queryType)
    {
        return $"{type.FullName}_{propertyName}_{queryType}";
    }
}

/// <summary>
/// 查询类型
/// </summary>
public enum QueryType
{
    Exact,      // 精确查询
    Range,      // 范围查询
    Pattern,    // 模式匹配
    Multiple,   // 复合查询
    Batch       // 批量查询
}

/// <summary>
/// 查询性能等级
/// </summary>
public enum QueryPerformance
{
    Low,        // 低性能
    Medium,     // 中等性能
    High,       // 高性能
    Optimal     // 最优性能
}

/// <summary>
/// 查询策略
/// </summary>
public class QueryStrategy
{
    /// <summary>
    /// 是否使用类型感知索引
    /// </summary>
    public bool UseTypedIndex { get; set; }

    /// <summary>
    /// 预估性能等级
    /// </summary>
    public QueryPerformance EstimatedPerformance { get; set; }

    /// <summary>
    /// 策略选择原因
    /// </summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// 策略生成时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 查询统计信息
/// </summary>
public class QueryStats
{
    // 总查询次数
    public int TotalQueries { get; set; }

    // 类型感知索引统计
    public int TypedIndexQueries { get; set; }
    public double TypedIndexTotalTime { get; set; }
    public double TypedIndexMinTime { get; set; }
    public double TypedIndexMaxTime { get; set; }

    // 传统索引统计
    public int TraditionalQueries { get; set; }
    public double TraditionalTotalTime { get; set; }
    public double TraditionalMinTime { get; set; }
    public double TraditionalMaxTime { get; set; }

    // 最后一次查询信息
    public int LastResultCount { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// 类型感知索引平均执行时间
    /// </summary>
    public double TypedIndexAvgTime => 
        TypedIndexQueries > 0 ? TypedIndexTotalTime / TypedIndexQueries : 0;

    /// <summary>
    /// 传统索引平均执行时间
    /// </summary>
    public double TraditionalAvgTime => 
        TraditionalQueries > 0 ? TraditionalTotalTime / TraditionalQueries : 0;

    /// <summary>
    /// 是否有两种索引的性能数据对比
    /// </summary>
    public bool HasBothIndexData => TypedIndexQueries > 0 && TraditionalQueries > 0;

    /// <summary>
    /// 性能提升倍数（类型感知索引相对于传统索引）
    /// </summary>
    public double PerformanceImprovement =>
        HasBothIndexData && TypedIndexAvgTime > 0 ? TraditionalAvgTime / TypedIndexAvgTime : 1.0;
}
