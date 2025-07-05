using System.Collections.Concurrent;
using System.Collections.Generic;
using DH.ORM;

namespace DH.Data.Cache
{
    /// <summary>
    /// 高并发索引管理器
    /// 提供读写分离锁、分段锁机制和批量操作优化
    /// </summary>
    public static class ConcurrentIndexManager
    {
        // 读写分离锁
        private static readonly ReaderWriterLockSlim _indexLock = new();
        
        // 分段锁机制：根据键的哈希值分段
        private static readonly ConcurrentDictionary<int, ReaderWriterLockSlim> _segmentLocks = new();
        private const int SEGMENT_COUNT = 32; // 分段数量
        
        // 线程安全的索引存储
        private static readonly ConcurrentDictionary<string, ConcurrentHashSet<long>> _concurrentIndexList = new();
        
        static ConcurrentIndexManager()
        {
            // 初始化分段锁
            for (int i = 0; i < SEGMENT_COUNT; i++)
            {
                _segmentLocks[i] = new ReaderWriterLockSlim();
            }
        }

        /// <summary>
        /// 获取键的分段索引
        /// </summary>
        private static int GetSegmentIndex(string key)
        {
            return Math.Abs(key.GetHashCode()) % SEGMENT_COUNT;
        }

        /// <summary>
        /// 获取分段锁
        /// </summary>
        private static ReaderWriterLockSlim GetSegmentLock(string key)
        {
            return _segmentLocks[GetSegmentIndex(key)];
        }

        /// <summary>
        /// 添加索引条目（单个）
        /// </summary>
        public static void AddIndex(string key, long id)
        {
            var segmentLock = GetSegmentLock(key);
            segmentLock.EnterWriteLock();
            try
            {
                var idSet = _concurrentIndexList.GetOrAdd(key, _ => new ConcurrentHashSet<long>());
                idSet.Add(id);
            }
            finally
            {
                segmentLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 移除索引条目（单个）
        /// </summary>
        public static void RemoveIndex(string key, long id)
        {
            var segmentLock = GetSegmentLock(key);
            segmentLock.EnterWriteLock();
            try
            {
                if (_concurrentIndexList.TryGetValue(key, out var idSet))
                {
                    idSet.Remove(id);
                    // 如果集合为空，移除键
                    if (idSet.Count == 0)
                    {
                        _concurrentIndexList.TryRemove(key, out _);
                        idSet.Dispose();
                    }
                }
            }
            finally
            {
                segmentLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 批量添加索引条目
        /// </summary>
        public static void BatchAddIndex(string key, IEnumerable<long> ids)
        {
            var segmentLock = GetSegmentLock(key);
            segmentLock.EnterWriteLock();
            try
            {
                var idSet = _concurrentIndexList.GetOrAdd(key, _ => new ConcurrentHashSet<long>());
                foreach (var id in ids)
                {
                    idSet.Add(id);
                }
            }
            finally
            {
                segmentLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 批量移除索引条目
        /// </summary>
        public static void BatchRemoveIndex(string key, IEnumerable<long> ids)
        {
            var segmentLock = GetSegmentLock(key);
            segmentLock.EnterWriteLock();
            try
            {
                if (_concurrentIndexList.TryGetValue(key, out var idSet))
                {
                    foreach (var id in ids)
                    {
                        idSet.Remove(id);
                    }
                    // 如果集合为空，移除键
                    if (idSet.Count == 0)
                    {
                        _concurrentIndexList.TryRemove(key, out _);
                        idSet.Dispose();
                    }
                }
            }
            finally
            {
                segmentLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 获取索引条目（读操作）
        /// </summary>
        public static HashSet<long> GetIndex(string key)
        {
            var segmentLock = GetSegmentLock(key);
            segmentLock.EnterReadLock();
            try
            {
                return _concurrentIndexList.TryGetValue(key, out var idSet) ? idSet.ToHashSet() : new HashSet<long>();
            }
            finally
            {
                segmentLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 检查索引是否存在
        /// </summary>
        public static bool ContainsIndex(string key)
        {
            var segmentLock = GetSegmentLock(key);
            segmentLock.EnterReadLock();
            try
            {
                return _concurrentIndexList.ContainsKey(key);
            }
            finally
            {
                segmentLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 批量更新索引（事务性操作）
        /// </summary>
        public static void BatchUpdateIndexes(Dictionary<string, (IEnumerable<long> toAdd, IEnumerable<long> toRemove)> operations)
        {
            // 按分段分组操作，减少锁竞争
            var segmentOperations = new Dictionary<int, List<(string key, IEnumerable<long> toAdd, IEnumerable<long> toRemove)>>();
            
            foreach (var operation in operations)
            {
                var segmentIndex = GetSegmentIndex(operation.Key);
                if (!segmentOperations.ContainsKey(segmentIndex))
                {
                    segmentOperations[segmentIndex] = new List<(string, IEnumerable<long>, IEnumerable<long>)>();
                }
                segmentOperations[segmentIndex].Add((operation.Key, operation.Value.toAdd, operation.Value.toRemove));
            }

            // 并行处理不同分段的操作
            Parallel.ForEach(segmentOperations, segmentGroup =>
            {
                var segmentLock = _segmentLocks[segmentGroup.Key];
                segmentLock.EnterWriteLock();
                try
                {
                    foreach (var (key, toAdd, toRemove) in segmentGroup.Value)
                    {
                        var idSet = _concurrentIndexList.GetOrAdd(key, _ => new ConcurrentHashSet<long>());
                        
                        // 批量添加
                        foreach (var id in toAdd)
                        {
                            idSet.Add(id);
                        }
                        
                        // 批量移除
                        foreach (var id in toRemove)
                        {
                            idSet.Remove(id);
                        }
                        
                        // 如果集合为空，移除键
                        if (idSet.Count == 0)
                        {
                            _concurrentIndexList.TryRemove(key, out _);
                            idSet.Dispose();
                        }
                    }
                }
                finally
                {
                    segmentLock.ExitWriteLock();
                }
            });
        }

        /// <summary>
        /// 获取索引统计信息
        /// </summary>
        public static Dictionary<string, int> GetIndexStatistics()
        {
            var stats = new Dictionary<string, int>();
            
            // 使用全局读锁获取统计信息
            _indexLock.EnterReadLock();
            try
            {
                foreach (var kvp in _concurrentIndexList)
                {
                    stats[kvp.Key] = kvp.Value.Count;
                }
            }
            finally
            {
                _indexLock.ExitReadLock();
            }
            
            return stats;
        }

        /// <summary>
        /// 清理空索引
        /// </summary>
        public static void CleanupEmptyIndexes()
        {
            var emptyKeys = new List<string>();
            
            // 找出空的索引键
            foreach (var kvp in _concurrentIndexList)
            {
                if (kvp.Value.Count == 0)
                {
                    emptyKeys.Add(kvp.Key);
                }
            }
            
            // 移除空索引
            foreach (var key in emptyKeys)
            {
                var segmentLock = GetSegmentLock(key);
                segmentLock.EnterWriteLock();
                try
                {
                    if (_concurrentIndexList.TryGetValue(key, out var idSet) && idSet.Count == 0)
                    {
                        _concurrentIndexList.TryRemove(key, out _);
                        idSet.Dispose();
                    }
                }
                finally
                {
                    segmentLock.ExitWriteLock();
                }
            }
        }

        /// <summary>
        /// 获取所有索引键
        /// </summary>
        public static IEnumerable<string> GetAllIndexKeys()
        {
            _indexLock.EnterReadLock();
            try
            {
                return _concurrentIndexList.Keys.ToList();
            }
            finally
            {
                _indexLock.ExitReadLock();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public static void Dispose()
        {
            _indexLock?.Dispose();
            foreach (var segmentLock in _segmentLocks.Values)
            {
                segmentLock?.Dispose();
            }
            foreach (var idSet in _concurrentIndexList.Values)
            {
                idSet?.Dispose();
            }
            _concurrentIndexList.Clear();
        }
    }
}
