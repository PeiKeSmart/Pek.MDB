using System.Collections.Concurrent;
using System.Text.Json;
using DH.Data.Cache.TypedIndex;

namespace DH.Data.Cache;

/// <summary>
/// 增量持久化管理器 - 优化数据保存和加载性能
/// </summary>
public static class IncrementalPersistenceManager
{
    // 变更日志缓存
    private static readonly ConcurrentQueue<ChangeEntry> _changeLog = new();
    
    // 最后保存时间
    private static DateTime _lastSaveTime = DateTime.Now;
    
    // 配置参数
    private static readonly IncrementalPersistenceConfig _config = new();
    
    // 后台保存任务
    private static Timer? _backgroundTimer;
    
    // 是否启用增量持久化
    private static volatile bool _enabled = false;

    /// <summary>
    /// 启用增量持久化（内部使用，自动优化）
    /// </summary>
    /// <param name="config">配置参数</param>
    internal static void EnableIncrementalPersistence(IncrementalPersistenceConfig? config = null)
    {
        _config.CopyFrom(config ?? new IncrementalPersistenceConfig());
        _enabled = true;
        
        // 启动后台保存任务
        if (_config.EnableBackgroundSave)
        {
            _backgroundTimer = new Timer(BackgroundSaveCallback, null, 
                _config.BackgroundSaveInterval, 
                _config.BackgroundSaveInterval);
        }
    }

    /// <summary>
    /// 禁用增量持久化（内部使用）
    /// </summary>
    internal static void DisableIncrementalPersistence()
    {
        _enabled = false;
        _backgroundTimer?.Dispose();
        _backgroundTimer = null;
    }

    /// <summary>
    /// 记录变更
    /// </summary>
    /// <param name="entry">变更条目</param>
    public static void RecordChange(ChangeEntry entry)
    {
        if (!_enabled) return;
        
        entry.Timestamp = DateTime.Now;
        _changeLog.Enqueue(entry);
        
        // 如果变更日志过大，触发保存
        if (_changeLog.Count >= _config.MaxChangeLogSize)
        {
            _ = Task.Run(SaveChangesAsync);
        }
    }

    /// <summary>
    /// 保存变更到文件
    /// </summary>
    /// <returns>保存结果</returns>
    public static async Task<bool> SaveChangesAsync()
    {
        if (!_enabled) return false;
        
        try
        {
            var changes = new List<ChangeEntry>();
            
            // 取出所有变更
            while (_changeLog.TryDequeue(out var change))
            {
                changes.Add(change);
            }
            
            if (changes.Count == 0) return true;
            
            // 按时间排序
            changes.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            
            // 保存到文件
            var changeLogPath = Path.Combine(_config.DataDirectory, "changes.log");
            var json = JsonSerializer.Serialize(changes, new JsonSerializerOptions 
            { 
                WriteIndented = false 
            });
            
            await Task.Run(() => System.IO.File.AppendAllText(changeLogPath, json + Environment.NewLine)).ConfigureAwait(false);
            
            _lastSaveTime = DateTime.Now;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save change log: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从变更日志恢复数据
    /// </summary>
    /// <returns>恢复结果</returns>
    public static async Task<bool> LoadFromChangeLogAsync()
    {
        if (!_enabled) return false;
        
        try
        {
            var changeLogPath = Path.Combine(_config.DataDirectory, "changes.log");
            if (!System.IO.File.Exists(changeLogPath)) return true;
            
            var lines = await Task.Run(() => System.IO.File.ReadAllLines(changeLogPath)).ConfigureAwait(false);
            var processedChanges = 0;
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                try
                {
                    var changes = JsonSerializer.Deserialize<List<ChangeEntry>>(line);
                    if (changes != null)
                    {
                        await ApplyChangesAsync(changes).ConfigureAwait(false);
                        processedChanges += changes.Count;
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Failed to parse change log line: {ex.Message}");
                }
            }
            
            Console.WriteLine($"Recovered {processedChanges} changes from change log");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to recover from change log: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 应用变更
    /// </summary>
    /// <param name="changes">变更列表</param>
    private static async Task ApplyChangesAsync(List<ChangeEntry> changes)
    {
        await Task.Run(() =>
        {
            foreach (var change in changes)
            {
                try
                {
                    switch (change.OperationType)
                    {
                        case ChangeOperationType.Insert:
                            // 重新插入对象
                            if (change.NewData != null)
                            {
                                var obj = JsonSerializer.Deserialize<CacheObject>(change.NewData);
                                if (obj != null)
                                {
                                    MemoryDB.Insert(obj);
                                }
                            }
                            break;
                        
                        case ChangeOperationType.Update:
                            // 更新对象
                            if (change.NewData != null)
                            {
                                var obj = JsonSerializer.Deserialize<CacheObject>(change.NewData);
                                if (obj != null)
                                {
                                    MemoryDB.Update(obj);
                                }
                            }
                            break;
                        
                        case ChangeOperationType.Delete:
                            // 删除对象
                            if (change.ObjectType != null)
                            {
                                var type = Type.GetType(change.ObjectType);
                                if (type != null)
                                {
                                    typeof(MemoryDB).GetMethod("Delete")?.MakeGenericMethod(type)?.Invoke(null, new object[] { change.ObjectId });
                                }
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to apply change (ID: {change.ObjectId}): {ex.Message}");
                }
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 清理过期的变更日志
    /// </summary>
    /// <returns>清理结果</returns>
    public static async Task<bool> CleanupExpiredChangesAsync()
    {
        if (!_enabled) return false;
        
        try
        {
            var changeLogPath = Path.Combine(_config.DataDirectory, "changes.log");
            if (!System.IO.File.Exists(changeLogPath)) return true;
            
            var lines = await Task.Run(() => System.IO.File.ReadAllLines(changeLogPath)).ConfigureAwait(false);
            var validLines = new List<string>();
            var cutoffTime = DateTime.Now.AddDays(-_config.ChangeLogRetentionDays);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                try
                {
                    var changes = JsonSerializer.Deserialize<List<ChangeEntry>>(line);
                    if (changes != null && changes.Any(c => c.Timestamp > cutoffTime))
                    {
                        validLines.Add(line);
                    }
                }
                catch (JsonException)
                {
                    // 忽略无效行
                }
            }
            
            // 重写日志文件
            await Task.Run(() => System.IO.File.WriteAllLines(changeLogPath, validLines)).ConfigureAwait(false);
            
            Console.WriteLine($"Cleanup change log completed, kept {validLines.Count} lines");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to cleanup change log: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 创建数据快照
    /// </summary>
    /// <returns>快照文件路径</returns>
    public static async Task<string?> CreateSnapshotAsync()
    {
        if (!_enabled) return null;
        
        try
        {
            var snapshotPath = Path.Combine(_config.DataDirectory, $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            
            // 获取所有数据
            var allData = new Dictionary<string, object>();
            
            // 这里需要实际的数据获取逻辑
            // 为了示例，我们创建一个简单的快照结构
            allData["timestamp"] = DateTime.Now;
            allData["version"] = "1.0";
            
            var json = JsonSerializer.Serialize(allData, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await Task.Run(() => System.IO.File.WriteAllText(snapshotPath, json)).ConfigureAwait(false);
            
            Console.WriteLine($"Creating data snapshot: {snapshotPath}");
            return snapshotPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create data snapshot: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 后台保存回调
    /// </summary>
    /// <param name="state">状态对象</param>
    private static void BackgroundSaveCallback(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SaveChangesAsync().ConfigureAwait(false);
                
                // 定期清理过期日志
                if (DateTime.Now.Hour == 2 && DateTime.Now.Minute < 10) // 凌晨2点清理
                {
                    await CleanupExpiredChangesAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Background save failed: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 获取持久化统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    public static PersistenceStats GetStatistics()
    {
        return new PersistenceStats
        {
            IsEnabled = _enabled,
            PendingChanges = _changeLog.Count,
            LastSaveTime = _lastSaveTime,
            BackgroundSaveEnabled = _config.EnableBackgroundSave,
            BackgroundSaveInterval = _config.BackgroundSaveInterval
        };
    }
}

/// <summary>
/// 变更条目
/// </summary>
public class ChangeEntry
{
    /// <summary>
    /// 对象ID
    /// </summary>
    public long ObjectId { get; set; }

    /// <summary>
    /// 对象类型
    /// </summary>
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>
    /// 操作类型
    /// </summary>
    public ChangeOperationType OperationType { get; set; }

    /// <summary>
    /// 旧数据（JSON格式）
    /// </summary>
    public string? OldData { get; set; }

    /// <summary>
    /// 新数据（JSON格式）
    /// </summary>
    public string? NewData { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 变更操作类型
/// </summary>
public enum ChangeOperationType
{
    /// <summary>
    /// 插入
    /// </summary>
    Insert,
    
    /// <summary>
    /// 更新
    /// </summary>
    Update,
    
    /// <summary>
    /// 删除
    /// </summary>
    Delete
}

/// <summary>
/// 增量持久化配置
/// </summary>
public class IncrementalPersistenceConfig
{
    /// <summary>
    /// 数据目录
    /// </summary>
    public string DataDirectory { get; set; } = "Data";

    /// <summary>
    /// 是否启用后台保存
    /// </summary>
    public bool EnableBackgroundSave { get; set; } = true;

    /// <summary>
    /// 后台保存间隔
    /// </summary>
    public TimeSpan BackgroundSaveInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大变更日志大小
    /// </summary>
    public int MaxChangeLogSize { get; set; } = 1000;

    /// <summary>
    /// 变更日志保留天数
    /// </summary>
    public int ChangeLogRetentionDays { get; set; } = 7;

    /// <summary>
    /// 从另一个配置复制设置
    /// </summary>
    /// <param name="other">其他配置</param>
    public void CopyFrom(IncrementalPersistenceConfig other)
    {
        DataDirectory = other.DataDirectory;
        EnableBackgroundSave = other.EnableBackgroundSave;
        BackgroundSaveInterval = other.BackgroundSaveInterval;
        MaxChangeLogSize = other.MaxChangeLogSize;
        ChangeLogRetentionDays = other.ChangeLogRetentionDays;
    }
}

/// <summary>
/// 持久化统计信息
/// </summary>
public class PersistenceStats
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 待处理变更数量
    /// </summary>
    public int PendingChanges { get; set; }

    /// <summary>
    /// 最后保存时间
    /// </summary>
    public DateTime LastSaveTime { get; set; }

    /// <summary>
    /// 是否启用后台保存
    /// </summary>
    public bool BackgroundSaveEnabled { get; set; }

    /// <summary>
    /// 后台保存间隔
    /// </summary>
    public TimeSpan BackgroundSaveInterval { get; set; }
}
