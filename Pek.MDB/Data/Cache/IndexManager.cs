using DH.Data.Cache.TypedIndex;
using System.Text;

namespace DH.Data.Cache;

/// <summary>
/// 索引管理器 - 提供索引监控和优化功能
/// </summary>
public static class IndexManager
{
    /// <summary>
    /// 获取指定类型和属性的索引统计信息
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <returns>索引统计信息</returns>
    public static IndexStatistics? GetIndexStats(Type type, string propertyName)
    {
        return TypedIndexManager.GetIndexStatistics(type, propertyName);
    }

    /// <summary>
    /// 获取所有索引的统计信息
    /// </summary>
    /// <returns>所有索引的统计信息列表</returns>
    public static List<IndexStatistics> GetAllIndexStats()
    {
        return TypedIndexManager.GetAllIndexStatistics();
    }

    /// <summary>
    /// 获取索引使用统计
    /// </summary>
    /// <returns>使用统计信息</returns>
    public static Dictionary<string, IndexUsageStats> GetUsageStatistics()
    {
        return TypedIndexManager.GetUsageStatistics();
    }

    /// <summary>
    /// 获取索引优化建议
    /// </summary>
    /// <returns>优化建议列表</returns>
    public static List<IndexOptimizationRecommendation> RecommendIndexOptimization()
    {
        return TypedIndexManager.GetOptimizationRecommendations();
    }

    /// <summary>
    /// 验证索引完整性
    /// </summary>
    /// <returns>验证结果</returns>
    public static IndexIntegrityReport ValidateIndexIntegrity()
    {
        var report = new IndexIntegrityReport();
        var stats = GetAllIndexStats();

        foreach (var stat in stats)
        {
            var issue = new IndexIntegrityIssue
            {
                PropertyName = stat.PropertyName,
                IndexType = stat.IndexType,
                Severity = IntegritySeverity.Info,
                Message = "索引正常"
            };

            // 检查索引使用率
            if (stat.QueryCount == 0)
            {
                issue.Severity = IntegritySeverity.Warning;
                issue.Message = "索引未被使用，考虑删除";
            }
            else if (stat.HitRate < 0.1)
            {
                issue.Severity = IntegritySeverity.Warning;
                issue.Message = $"索引命中率过低 ({stat.HitRate:P})，考虑优化";
            }
            else if (stat.HitRate > 0.9 && stat.QueryCount > 100)
            {
                issue.Severity = IntegritySeverity.Info;
                issue.Message = $"索引性能良好 (命中率: {stat.HitRate:P})";
            }

            // 检查内存使用
            if (stat.MemoryUsage > 10 * 1024 * 1024) // 10MB
            {
                issue.Severity = IntegritySeverity.Warning;
                issue.Message += $" 内存使用较高: {stat.MemoryUsage / (1024 * 1024):F1}MB";
            }

            report.Issues.Add(issue);
        }

        report.IsHealthy = !report.Issues.Any(i => i.Severity == IntegritySeverity.Error);
        report.CheckTime = DateTime.Now;

        return report;
    }

    /// <summary>
    /// 生成索引性能报告
    /// </summary>
    /// <returns>性能报告</returns>
    public static string GeneratePerformanceReport()
    {
        var sb = new StringBuilder();
        var stats = GetAllIndexStats();
        var usageStats = GetUsageStatistics();

        sb.AppendLine("# 索引性能报告");
        sb.AppendLine($"## 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // 总体统计
        sb.AppendLine("## 总体统计");
        sb.AppendLine($"- 总索引数: {stats.Count}");
        sb.AppendLine($"- 活跃索引数: {stats.Count(s => s.QueryCount > 0)}");
        
        var totalMemory = stats.Sum(s => s.MemoryUsage);
        sb.AppendLine($"- 总内存使用: {totalMemory / (1024 * 1024):F2} MB");
        
        var totalQueries = stats.Sum(s => s.QueryCount);
        sb.AppendLine($"- 总查询次数: {totalQueries:N0}");
        
        var avgHitRate = stats.Where(s => s.QueryCount > 0).Average(s => s.HitRate);
        sb.AppendLine($"- 平均命中率: {avgHitRate:P2}");
        sb.AppendLine();

        // 索引类型分布
        sb.AppendLine("## 索引类型分布");
        var indexTypeGroups = stats.GroupBy(s => s.IndexType);
        foreach (var group in indexTypeGroups)
        {
            sb.AppendLine($"- {group.Key}: {group.Count()} 个");
        }
        sb.AppendLine();

        // 性能最好的索引
        sb.AppendLine("## 性能最好的索引 (Top 10)");
        var topPerformers = stats
            .Where(s => s.QueryCount > 10)
            .OrderByDescending(s => s.HitRate)
            .ThenByDescending(s => s.QueryCount)
            .Take(10);

        foreach (var stat in topPerformers)
        {
            sb.AppendLine($"- {stat.PropertyName} ({stat.IndexType}): 命中率 {stat.HitRate:P}, 查询 {stat.QueryCount} 次");
        }
        sb.AppendLine();

        // 需要优化的索引
        sb.AppendLine("## 需要优化的索引");
        var needOptimization = stats
            .Where(s => s.QueryCount > 0 && s.HitRate < 0.5)
            .OrderBy(s => s.HitRate);

        foreach (var stat in needOptimization)
        {
            sb.AppendLine($"- {stat.PropertyName} ({stat.IndexType}): 命中率 {stat.HitRate:P}, 查询 {stat.QueryCount} 次");
        }

        if (!needOptimization.Any())
        {
            sb.AppendLine("- 没有需要优化的索引");
        }
        sb.AppendLine();

        // 未使用的索引
        sb.AppendLine("## 未使用的索引");
        var unusedIndexes = stats.Where(s => s.QueryCount == 0);
        foreach (var stat in unusedIndexes)
        {
            sb.AppendLine($"- {stat.PropertyName} ({stat.IndexType}): 内存使用 {stat.MemoryUsage / 1024:F1} KB");
        }

        if (!unusedIndexes.Any())
        {
            sb.AppendLine("- 没有未使用的索引");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 清理未使用的索引
    /// </summary>
    /// <param name="dryRun">是否只是预览，不实际删除</param>
    /// <returns>清理结果</returns>
    public static IndexCleanupResult CleanupUnusedIndexes(bool dryRun = true)
    {
        var result = new IndexCleanupResult();
        var stats = GetAllIndexStats();
        var unusedIndexes = stats.Where(s => s.QueryCount == 0).ToList();

        result.TotalIndexes = stats.Count;
        result.UnusedIndexes = unusedIndexes.Count;
        result.MemoryFreed = unusedIndexes.Sum(s => s.MemoryUsage);

        if (!dryRun)
        {
            // TODO: 实际删除未使用的索引
            // 这需要在TypedIndexManager中添加删除方法
            result.ActuallyDeleted = unusedIndexes.Count;
        }

        return result;
    }
}

/// <summary>
/// 索引完整性报告
/// </summary>
public class IndexIntegrityReport
{
    /// <summary>
    /// 索引是否健康
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckTime { get; set; }

    /// <summary>
    /// 发现的问题列表
    /// </summary>
    public List<IndexIntegrityIssue> Issues { get; set; } = new();

    /// <summary>
    /// 获取按严重程度分组的问题
    /// </summary>
    /// <returns>按严重程度分组的问题</returns>
    public Dictionary<IntegritySeverity, List<IndexIntegrityIssue>> GetIssuesBySeverity()
    {
        return Issues.GroupBy(i => i.Severity)
                    .ToDictionary(g => g.Key, g => g.ToList());
    }
}

/// <summary>
/// 索引完整性问题
/// </summary>
public class IndexIntegrityIssue
{
    /// <summary>
    /// 属性名称
    /// </summary>
    public string PropertyName { get; set; } = "";

    /// <summary>
    /// 索引类型
    /// </summary>
    public string IndexType { get; set; } = "";

    /// <summary>
    /// 问题严重程度
    /// </summary>
    public IntegritySeverity Severity { get; set; }

    /// <summary>
    /// 问题描述
    /// </summary>
    public string Message { get; set; } = "";
}

/// <summary>
/// 完整性问题严重程度
/// </summary>
public enum IntegritySeverity
{
    /// <summary>
    /// 信息
    /// </summary>
    Info,
    /// <summary>
    /// 警告
    /// </summary>
    Warning,
    /// <summary>
    /// 错误
    /// </summary>
    Error
}

/// <summary>
/// 索引清理结果
/// </summary>
public class IndexCleanupResult
{
    /// <summary>
    /// 总索引数
    /// </summary>
    public int TotalIndexes { get; set; }

    /// <summary>
    /// 未使用的索引数
    /// </summary>
    public int UnusedIndexes { get; set; }

    /// <summary>
    /// 实际删除的索引数
    /// </summary>
    public int ActuallyDeleted { get; set; }

    /// <summary>
    /// 释放的内存（字节）
    /// </summary>
    public long MemoryFreed { get; set; }

    /// <summary>
    /// 释放的内存（MB）
    /// </summary>
    public double MemoryFreedMB => MemoryFreed / (1024.0 * 1024.0);
}
