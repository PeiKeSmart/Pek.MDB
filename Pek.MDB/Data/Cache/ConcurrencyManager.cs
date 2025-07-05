using System.Collections.Concurrent;
using DH.Data.Cache.TypedIndex;

namespace DH.Data.Cache;

/// <summary>
/// 高级并发控制管理器 - 提供更精细的锁控制和并发优化
/// </summary>
public static class ConcurrencyManager
{
    // 分段锁字典
    private static readonly ConcurrentDictionary<string, ReaderWriterLockSlim> _segmentLocks = new();
    
    // 类型级别的锁
    private static readonly ConcurrentDictionary<Type, ReaderWriterLockSlim> _typeLocks = new();
    
    // 锁超时时间
    private static readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(30);
    
    // 并发统计
    private static readonly ConcurrentDictionary<string, ConcurrencyStats> _stats = new();

    /// <summary>
    /// 获取分段锁
    /// </summary>
    /// <param name="key">锁键</param>
    /// <returns>读写锁</returns>
    private static ReaderWriterLockSlim GetSegmentLock(string key)
    {
        return _segmentLocks.GetOrAdd(key, _ => new ReaderWriterLockSlim());
    }

    /// <summary>
    /// 获取类型锁
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>读写锁</returns>
    private static ReaderWriterLockSlim GetTypeLock(Type type)
    {
        return _typeLocks.GetOrAdd(type, _ => new ReaderWriterLockSlim());
    }

    /// <summary>
    /// 执行读操作
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="key">锁键</param>
    /// <param name="action">读操作</param>
    /// <returns>操作结果</returns>
    public static T ExecuteRead<T>(string key, Func<T> action)
    {
        var lockSlim = GetSegmentLock(key);
        var stats = GetStats(key);
        
        stats.ReadRequests++;
        var startTime = DateTime.Now;
        
        try
        {
            if (lockSlim.TryEnterReadLock(_lockTimeout))
            {
                try
                {
                    var result = action();
                    stats.SuccessfulReads++;
                    return result;
                }
                finally
                {
                    lockSlim.ExitReadLock();
                }
            }
            else
            {
                stats.ReadTimeouts++;
                throw new TimeoutException($"获取读锁超时: {key}");
            }
        }
        finally
        {
            stats.AverageReadTime = CalculateAverageTime(stats.AverageReadTime, 
                stats.SuccessfulReads, DateTime.Now - startTime);
        }
    }

    /// <summary>
    /// 执行写操作
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="key">锁键</param>
    /// <param name="action">写操作</param>
    /// <returns>操作结果</returns>
    public static T ExecuteWrite<T>(string key, Func<T> action)
    {
        var lockSlim = GetSegmentLock(key);
        var stats = GetStats(key);
        
        stats.WriteRequests++;
        var startTime = DateTime.Now;
        
        try
        {
            if (lockSlim.TryEnterWriteLock(_lockTimeout))
            {
                try
                {
                    var result = action();
                    stats.SuccessfulWrites++;
                    return result;
                }
                finally
                {
                    lockSlim.ExitWriteLock();
                }
            }
            else
            {
                stats.WriteTimeouts++;
                throw new TimeoutException($"获取写锁超时: {key}");
            }
        }
        finally
        {
            stats.AverageWriteTime = CalculateAverageTime(stats.AverageWriteTime, 
                stats.SuccessfulWrites, DateTime.Now - startTime);
        }
    }

    /// <summary>
    /// 执行类型级别的读操作
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="type">类型</param>
    /// <param name="action">读操作</param>
    /// <returns>操作结果</returns>
    public static T ExecuteTypeRead<T>(Type type, Func<T> action)
    {
        var lockSlim = GetTypeLock(type);
        var key = $"Type:{type.Name}";
        var stats = GetStats(key);
        
        stats.ReadRequests++;
        var startTime = DateTime.Now;
        
        try
        {
            if (lockSlim.TryEnterReadLock(_lockTimeout))
            {
                try
                {
                    var result = action();
                    stats.SuccessfulReads++;
                    return result;
                }
                finally
                {
                    lockSlim.ExitReadLock();
                }
            }
            else
            {
                stats.ReadTimeouts++;
                throw new TimeoutException($"获取类型读锁超时: {type.Name}");
            }
        }
        finally
        {
            stats.AverageReadTime = CalculateAverageTime(stats.AverageReadTime, 
                stats.SuccessfulReads, DateTime.Now - startTime);
        }
    }

    /// <summary>
    /// 执行类型级别的写操作
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="type">类型</param>
    /// <param name="action">写操作</param>
    /// <returns>操作结果</returns>
    public static T ExecuteTypeWrite<T>(Type type, Func<T> action)
    {
        var lockSlim = GetTypeLock(type);
        var key = $"Type:{type.Name}";
        var stats = GetStats(key);
        
        stats.WriteRequests++;
        var startTime = DateTime.Now;
        
        try
        {
            if (lockSlim.TryEnterWriteLock(_lockTimeout))
            {
                try
                {
                    var result = action();
                    stats.SuccessfulWrites++;
                    return result;
                }
                finally
                {
                    lockSlim.ExitWriteLock();
                }
            }
            else
            {
                stats.WriteTimeouts++;
                throw new TimeoutException($"获取类型写锁超时: {type.Name}");
            }
        }
        finally
        {
            stats.AverageWriteTime = CalculateAverageTime(stats.AverageWriteTime, 
                stats.SuccessfulWrites, DateTime.Now - startTime);
        }
    }

    /// <summary>
    /// 并行执行多个读操作
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="operations">操作列表</param>
    /// <returns>操作结果</returns>
    public static async Task<List<T>> ExecuteParallelReads<T>(List<(string Key, Func<T> Action)> operations)
    {
        var tasks = operations.Select(op => Task.Run(() => ExecuteRead(op.Key, op.Action)));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    /// <summary>
    /// 批量执行写操作（串行）
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="operations">操作列表</param>
    /// <returns>操作结果</returns>
    public static List<T> ExecuteBatchWrites<T>(List<(string Key, Func<T> Action)> operations)
    {
        var results = new List<T>();
        
        foreach (var (key, action) in operations)
        {
            var result = ExecuteWrite(key, action);
            results.Add(result);
        }
        
        return results;
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>统计信息</returns>
    private static ConcurrencyStats GetStats(string key)
    {
        return _stats.GetOrAdd(key, _ => new ConcurrencyStats { Key = key });
    }

    /// <summary>
    /// 计算平均时间
    /// </summary>
    /// <param name="currentAverage">当前平均时间</param>
    /// <param name="count">计数</param>
    /// <param name="newTime">新时间</param>
    /// <returns>新的平均时间</returns>
    private static TimeSpan CalculateAverageTime(TimeSpan currentAverage, long count, TimeSpan newTime)
    {
        if (count <= 1) return newTime;
        
        var totalTicks = currentAverage.Ticks * (count - 1) + newTime.Ticks;
        return new TimeSpan(totalTicks / count);
    }

    /// <summary>
    /// 获取所有并发统计信息
    /// </summary>
    /// <returns>统计信息列表</returns>
    public static List<ConcurrencyStats> GetAllStats()
    {
        return _stats.Values.ToList();
    }

    /// <summary>
    /// 获取并发统计信息
    /// </summary>
    /// <returns>并发统计信息</returns>
    public static ConcurrencyStats GetConcurrencyStats()
    {
        var allStats = GetAllStats();
        var totalStats = new ConcurrencyStats
        {
            Key = "Total",
            ReadRequests = allStats.Sum(s => s.ReadRequests),
            WriteRequests = allStats.Sum(s => s.WriteRequests),
            SuccessfulReads = allStats.Sum(s => s.SuccessfulReads),
            SuccessfulWrites = allStats.Sum(s => s.SuccessfulWrites),
            ReadTimeouts = allStats.Sum(s => s.ReadTimeouts),
            WriteTimeouts = allStats.Sum(s => s.WriteTimeouts),
            LastAccessed = DateTime.Now
        };

        if (allStats.Count > 0)
        {
            var avgReadTicks = allStats.Where(s => s.SuccessfulReads > 0)
                .Average(s => s.AverageReadTime.Ticks);
            var avgWriteTicks = allStats.Where(s => s.SuccessfulWrites > 0)
                .Average(s => s.AverageWriteTime.Ticks);

            totalStats.AverageReadTime = new TimeSpan((long)avgReadTicks);
            totalStats.AverageWriteTime = new TimeSpan((long)avgWriteTicks);
        }

        return totalStats;
    }

    /// <summary>
    /// 清理未使用的锁
    /// </summary>
    /// <returns>清理的锁数量</returns>
    public static int CleanupUnusedLocks()
    {
        var cleanedCount = 0;
        var cutoffTime = DateTime.Now.AddHours(-1);
        
        var keysToRemove = new List<string>();
        
        foreach (var kvp in _stats)
        {
            if (kvp.Value.LastAccessed < cutoffTime)
            {
                keysToRemove.Add(kvp.Key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            if (_stats.TryRemove(key, out _))
            {
                if (_segmentLocks.TryRemove(key, out var lockSlim))
                {
                    lockSlim.Dispose();
                    cleanedCount++;
                }
            }
        }
        
        return cleanedCount;
    }

    /// <summary>
    /// 生成并发性能报告
    /// </summary>
    /// <returns>性能报告</returns>
    public static string GenerateConcurrencyReport()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("=== 并发性能报告 ===");
        report.AppendLine($"报告时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine();
        
        var stats = GetAllStats().OrderByDescending(s => s.TotalRequests).ToList();
        
        report.AppendLine($"活跃锁数量: {_segmentLocks.Count}");
        report.AppendLine($"类型锁数量: {_typeLocks.Count}");
        report.AppendLine();
        
        report.AppendLine("锁使用统计:");
        report.AppendLine("键名                     | 读请求  | 写请求  | 读超时 | 写超时 | 平均读时间 | 平均写时间");
        report.AppendLine("-------------------------|---------|---------|--------|--------|------------|------------");
        
        foreach (var stat in stats.Take(20))
        {
            report.AppendLine($"{stat.Key,-24} | {stat.ReadRequests,7} | {stat.WriteRequests,7} | {stat.ReadTimeouts,6} | {stat.WriteTimeouts,6} | {stat.AverageReadTime.TotalMilliseconds,8:F1}ms | {stat.AverageWriteTime.TotalMilliseconds,8:F1}ms");
        }
        
        return report.ToString();
    }

    /// <summary>
    /// 释放所有资源
    /// </summary>
    public static void Dispose()
    {
        foreach (var lockSlim in _segmentLocks.Values)
        {
            lockSlim.Dispose();
        }
        _segmentLocks.Clear();
        
        foreach (var lockSlim in _typeLocks.Values)
        {
            lockSlim.Dispose();
        }
        _typeLocks.Clear();
        
        _stats.Clear();
    }
}

/// <summary>
/// 并发统计信息
/// </summary>
public class ConcurrencyStats
{
    /// <summary>
    /// 键名
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 读请求数
    /// </summary>
    public long ReadRequests { get; set; }

    /// <summary>
    /// 写请求数
    /// </summary>
    public long WriteRequests { get; set; }

    /// <summary>
    /// 成功读取数
    /// </summary>
    public long SuccessfulReads { get; set; }

    /// <summary>
    /// 成功写入数
    /// </summary>
    public long SuccessfulWrites { get; set; }

    /// <summary>
    /// 读超时数
    /// </summary>
    public long ReadTimeouts { get; set; }

    /// <summary>
    /// 写超时数
    /// </summary>
    public long WriteTimeouts { get; set; }

    /// <summary>
    /// 平均读时间
    /// </summary>
    public TimeSpan AverageReadTime { get; set; }

    /// <summary>
    /// 平均写时间
    /// </summary>
    public TimeSpan AverageWriteTime { get; set; }

    /// <summary>
    /// 最后访问时间
    /// </summary>
    public DateTime LastAccessed { get; set; } = DateTime.Now;

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests => ReadRequests + WriteRequests;

    /// <summary>
    /// 读成功率
    /// </summary>
    public double ReadSuccessRate => ReadRequests > 0 ? (double)SuccessfulReads / ReadRequests : 0;

    /// <summary>
    /// 写成功率
    /// </summary>
    public double WriteSuccessRate => WriteRequests > 0 ? (double)SuccessfulWrites / WriteRequests : 0;
}
