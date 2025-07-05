using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DH.Data.Cache;

/// <summary>
/// 查询缓存系统 - 缓存频繁查询结果以提升性能
/// </summary>
public static class QueryCache
{
    // 查询结果缓存
    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    
    // 类型变更通知（用于缓存失效）
    private static readonly ConcurrentDictionary<Type, long> _typeVersions = new();
    
    // 缓存策略配置
    private static CachePolicy _policy = new CachePolicy();
    
    // 缓存统计
    private static readonly ConcurrentDictionary<string, CacheStats> _stats = new();
    
    // 是否启用缓存
    private static volatile bool _enabled = true;

    /// <summary>
    /// 启用或禁用查询缓存
    /// </summary>
    /// <param name="enabled">是否启用</param>
    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            ClearAll();
        }
    }

    /// <summary>
    /// 检查是否启用查询缓存
    /// </summary>
    /// <returns>是否启用</returns>
    public static bool IsEnabled() => _enabled;

    /// <summary>
    /// 设置缓存策略
    /// </summary>
    /// <param name="policy">缓存策略</param>
    public static void SetCachePolicy(CachePolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        
        // 如果缓存大小限制变小，触发清理
        if (_cache.Count > _policy.MaxCacheSize)
        {
            CleanupCache();
        }
    }

    /// <summary>
    /// 获取当前缓存策略
    /// </summary>
    /// <returns>缓存策略</returns>
    public static CachePolicy GetCachePolicy() => _policy;

    /// <summary>
    /// 尝试从缓存获取查询结果
    /// </summary>
    /// <typeparam name="T">结果类型</typeparam>
    /// <param name="queryKey">查询键</param>
    /// <param name="queryFunc">查询函数</param>
    /// <returns>查询结果</returns>
    public static List<T> GetCachedResult<T>(string queryKey, Func<List<T>> queryFunc) where T : class
    {
        if (!_enabled)
        {
            return queryFunc();
        }

        var hashedKey = HashQueryKey(queryKey);
        var stats = GetOrCreateStats(hashedKey);

        // 检查缓存命中
        if (_cache.TryGetValue(hashedKey, out var entry))
        {
            // 检查过期时间
            if (DateTime.Now - entry.CreatedAt <= _policy.ExpireTime)
            {
                // 检查类型版本（数据是否已更新）
                var currentVersion = GetTypeVersion(typeof(T));
                if (entry.TypeVersion == currentVersion)
                {
                    // 更新访问时间和命中统计
                    entry.LastAccessed = DateTime.Now;
                    entry.HitCount++;
                    
                    stats.Hits++;
                    stats.LastAccessed = DateTime.Now;

                    // 反序列化结果
                    try
                    {
                        var result = JsonSerializer.Deserialize<List<T>>(entry.Data);
                        return result ?? new List<T>();
                    }
                    catch
                    {
                        // 反序列化失败，移除缓存项
                        _cache.TryRemove(hashedKey, out _);
                    }
                }
                else
                {
                    // 数据已过期，移除缓存项
                    _cache.TryRemove(hashedKey, out _);
                }
            }
            else
            {
                // 缓存过期，移除缓存项
                _cache.TryRemove(hashedKey, out _);
            }
        }

        // 缓存未命中，执行查询
        stats.Misses++;
        var queryResult = queryFunc();

        // 缓存结果（如果启用且结果不为空）
        if (_policy.CacheEmptyResults || (queryResult?.Count > 0))
        {
            CacheResult(hashedKey, queryResult, typeof(T));
        }

        return queryResult;
    }

    /// <summary>
    /// 基于查询参数的泛型缓存获取方法
    /// </summary>
    /// <typeparam name="T">结果类型</typeparam>
    /// <param name="type">查询的类型</param>
    /// <param name="propertyName">属性名</param>
    /// <param name="queryType">查询类型</param>
    /// <param name="value">查询值</param>
    /// <param name="queryFunc">查询函数</param>
    /// <returns>查询结果</returns>
    public static List<T> GetCachedQueryResult<T>(Type type, string propertyName, string queryType, 
        object? value, Func<List<T>> queryFunc) where T : class
    {
        var queryKey = GenerateQueryKey(type, propertyName, queryType, value);
        return GetCachedResult(queryKey, queryFunc);
    }

    /// <summary>
    /// 使缓存失效（当数据发生变更时调用）
    /// </summary>
    /// <param name="type">发生变更的数据类型</param>
    /// <param name="propertyName">可选：特定属性名，null表示整个类型</param>
    public static void InvalidateCache(Type type, string? propertyName = null)
    {
        if (!_enabled) return;

        // 更新类型版本
        _typeVersions.AddOrUpdate(type, 1, (_, v) => v + 1);

        // 如果指定了属性，只清理相关的缓存项
        if (!string.IsNullOrEmpty(propertyName))
        {
            var keysToRemove = _cache.Keys
                .Where(key => key.Contains($"{type.FullName}_{propertyName}_"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }
        else
        {
            // 清理整个类型相关的缓存项
            var keysToRemove = _cache.Keys
                .Where(key => key.Contains(type.FullName ?? ""))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// 清理所有缓存
    /// </summary>
    public static void ClearAll()
    {
        _cache.Clear();
        _stats.Clear();
        _typeVersions.Clear();
    }

    /// <summary>
    /// 清理过期缓存
    /// </summary>
    public static void CleanupCache()
    {
        var now = DateTime.Now;
        var keysToRemove = new List<string>();

        // 找出过期的缓存项
        foreach (var kvp in _cache)
        {
            var entry = kvp.Value;
            if (now - entry.CreatedAt > _policy.ExpireTime)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        // 如果启用LRU并且缓存大小超限，清理最少使用的项
        if (_policy.EnableLRU && _cache.Count > _policy.MaxCacheSize)
        {
            var lruEntries = _cache
                .OrderBy(kvp => kvp.Value.LastAccessed)
                .Take(_cache.Count - _policy.MaxCacheSize + keysToRemove.Count)
                .Select(kvp => kvp.Key);

            keysToRemove.AddRange(lruEntries);
        }

        // 移除过期和LRU项
        foreach (var key in keysToRemove.Distinct())
        {
            _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    /// <returns>缓存统计信息</returns>
    public static CacheStatistics GetCacheStatistics()
    {
        var totalHits = _stats.Values.Sum(s => s.Hits);
        var totalMisses = _stats.Values.Sum(s => s.Misses);
        var totalRequests = totalHits + totalMisses;

        return new CacheStatistics
        {
            IsEnabled = _enabled,
            TotalCacheEntries = _cache.Count,
            TotalRequests = totalRequests,
            CacheHits = totalHits,
            CacheMisses = totalMisses,
            HitRate = totalRequests > 0 ? (double)totalHits / totalRequests : 0,
            MemoryUsage = EstimateMemoryUsage(),
            Policy = _policy
        };
    }

    /// <summary>
    /// 生成缓存性能报告
    /// </summary>
    /// <returns>性能报告</returns>
    public static string GenerateCacheReport()
    {
        var sb = new StringBuilder();
        var statistics = GetCacheStatistics();

        sb.AppendLine("# 查询缓存报告");
        sb.AppendLine($"## 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("## 缓存配置");
        sb.AppendLine($"- 缓存状态: {(statistics.IsEnabled ? "启用" : "禁用")}");
        sb.AppendLine($"- 最大缓存项数: {statistics.Policy.MaxCacheSize}");
        sb.AppendLine($"- 过期时间: {statistics.Policy.ExpireTime.TotalMinutes:F1} 分钟");
        sb.AppendLine($"- LRU策略: {(statistics.Policy.EnableLRU ? "启用" : "禁用")}");
        sb.AppendLine($"- 缓存空结果: {(statistics.Policy.CacheEmptyResults ? "是" : "否")}");
        sb.AppendLine();

        sb.AppendLine("## 性能统计");
        sb.AppendLine($"- 缓存项总数: {statistics.TotalCacheEntries}");
        sb.AppendLine($"- 总请求数: {statistics.TotalRequests:N0}");
        sb.AppendLine($"- 缓存命中: {statistics.CacheHits:N0}");
        sb.AppendLine($"- 缓存未命中: {statistics.CacheMisses:N0}");
        sb.AppendLine($"- 命中率: {statistics.HitRate:P2}");
        sb.AppendLine($"- 内存使用: {statistics.MemoryUsage / 1024.0:F1} KB");
        sb.AppendLine();

        if (_stats.Any())
        {
            sb.AppendLine("## 热点查询 (Top 10)");
            var hotQueries = _stats
                .OrderByDescending(kvp => kvp.Value.Hits + kvp.Value.Misses)
                .Take(10);

            foreach (var query in hotQueries)
            {
                var stats = query.Value;
                var totalReq = stats.Hits + stats.Misses;
                var hitRate = totalReq > 0 ? (double)stats.Hits / totalReq : 0;
                sb.AppendLine($"- {query.Key.Substring(0, Math.Min(60, query.Key.Length))}...: " +
                    $"{totalReq} 次请求, 命中率 {hitRate:P}");
            }
        }

        return sb.ToString();
    }

    private static void CacheResult<T>(string hashedKey, List<T> result, Type type)
    {
        try
        {
            // 检查缓存大小限制
            if (_cache.Count >= _policy.MaxCacheSize)
            {
                CleanupCache();
                
                // 如果清理后仍然满，不缓存新结果
                if (_cache.Count >= _policy.MaxCacheSize)
                {
                    return;
                }
            }

            var serializedData = JsonSerializer.Serialize(result);
            var entry = new CacheEntry
            {
                Data = serializedData,
                CreatedAt = DateTime.Now,
                LastAccessed = DateTime.Now,
                TypeVersion = GetTypeVersion(type),
                HitCount = 0
            };

            _cache[hashedKey] = entry;
        }
        catch
        {
            // 序列化失败，不缓存
        }
    }

    private static long GetTypeVersion(Type type)
    {
        return _typeVersions.GetOrAdd(type, 0);
    }

    private static CacheStats GetOrCreateStats(string key)
    {
        return _stats.GetOrAdd(key, _ => new CacheStats());
    }

    private static string HashQueryKey(string queryKey)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(queryKey));
        return Convert.ToBase64String(hashedBytes);
    }

    private static string GenerateQueryKey(Type type, string propertyName, string queryType, object? value)
    {
        var valueStr = value?.ToString() ?? "null";
        return $"{type.FullName}_{propertyName}_{queryType}_{valueStr}";
    }

    private static long EstimateMemoryUsage()
    {
        long totalSize = 0;
        
        foreach (var entry in _cache.Values)
        {
            totalSize += entry.Data?.Length * sizeof(char) ?? 0; // 字符串大小估算
            totalSize += 64; // 其他字段的估算大小
        }

        return totalSize;
    }
}

/// <summary>
/// 缓存策略配置
/// </summary>
public class CachePolicy
{
    /// <summary>
    /// 缓存过期时间
    /// </summary>
    public TimeSpan ExpireTime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 最大缓存项数量
    /// </summary>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// 启用LRU（最近最少使用）淘汰策略
    /// </summary>
    public bool EnableLRU { get; set; } = true;

    /// <summary>
    /// 是否缓存空结果
    /// </summary>
    public bool CacheEmptyResults { get; set; } = false;
}

/// <summary>
/// 缓存项
/// </summary>
internal class CacheEntry
{
    public string Data { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessed { get; set; }
    public long TypeVersion { get; set; }
    public int HitCount { get; set; }
}

/// <summary>
/// 缓存统计信息（每个查询键）
/// </summary>
internal class CacheStats
{
    public int Hits { get; set; }
    public int Misses { get; set; }
    public DateTime LastAccessed { get; set; } = DateTime.Now;
}

/// <summary>
/// 缓存统计信息（全局）
/// </summary>
public class CacheStatistics
{
    public bool IsEnabled { get; set; }
    public int TotalCacheEntries { get; set; }
    public int TotalRequests { get; set; }
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public double HitRate { get; set; }
    public long MemoryUsage { get; set; }
    public CachePolicy Policy { get; set; } = new();
}
