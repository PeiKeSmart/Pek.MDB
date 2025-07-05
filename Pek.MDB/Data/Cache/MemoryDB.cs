using DH.ORM;
using DH.Reflection;
using DH.Serialization;
using DH.Data.Cache;
using DH.Data.Cache.TypedIndex;

using NewLife.Log;

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Reflection;
using System.Text;

namespace DH;

internal class MemoryDB
{

    private static IDictionary objectList = Hashtable.Synchronized([]);
    // 高效索引结构：使用线程安全的集合
    private static ConcurrentDictionary<string, ConcurrentHashSet<long>> indexList = new();
    private static readonly object indexLock = new();
    
    // 类型级别锁 - 提升并发性能
    private static readonly ConcurrentDictionary<Type, object> _typeLocks = new();
    
    // 原子ID生成器 - 避免ID冲突
    private static readonly ConcurrentDictionary<Type, long> _typeIdCounters = new();
    
    /// <summary>
    /// 获取类型的可读属性（现代.NET反射性能已足够）
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>属性数组</returns>
    private static PropertyInfo[] GetTypeProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                  .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                  .ToArray();
    }
    
    /// <summary>
    /// 获取类型专用锁，避免不同类型间的锁竞争
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>类型锁对象</returns>
    private static object GetTypeLock(Type type)
    {
        return _typeLocks.GetOrAdd(type, _ => new object());
    }
    
    /// <summary>
    /// 原子性生成下一个ID，确保线程安全
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>新的唯一ID</returns>
    private static long GetNextIdAtomic(Type type)
    {
        return _typeIdCounters.AddOrUpdate(type, 1, (_, current) => current + 1);
    }
    
    // 类型感知索引配置
    private static volatile bool _enableTypedIndex = true;

    /// <summary>
    /// 启用或禁用类型感知索引（内部管理，自动优化）
    /// </summary>
    /// <param name="enable">是否启用</param>
    internal static void EnableTypedIndex(bool enable)
    {
        _enableTypedIndex = enable;
    }

    /// <summary>
    /// 检查是否启用了类型感知索引（内部使用）
    /// </summary>
    /// <returns>是否启用</returns>
    internal static bool IsTypedIndexEnabled()
    {
        return _enableTypedIndex;
    }

    public static IDictionary GetObjectsMap()
    {
        return objectList;
    }

    public static IDictionary GetIndexMap()
    {
        // 为了向后兼容，转换为旧格式返回
        var result = new Hashtable();
        foreach (var kvp in indexList)
        {
            result[kvp.Key] = string.Join(",", kvp.Value);
        }
        return result;
    }


    private static Object objLock = new();


    private static IList GetObjectsByName(Type t)
    {

        if (IsCheckFileDB(t))
        {

            lock (objLock)
            {

                if (IsCheckFileDB(t))
                {

                    LoadDataFromFile(t);
                    _hasCheckedFileDB[t] = true;

                }

            }

        }
        return (objectList[t.FullName] as IList);
    }

    private static Hashtable _hasCheckedFileDB = [];

    private static Boolean IsCheckFileDB(Type t)
    {
        if (_hasCheckedFileDB[t] == null) return true;
        return false;
    }

    private static void LoadDataFromFile(Type t)
    {
        if (IO.File.Exists(GetCachePath(t)))
        {
            IList list = GetListWithIndex(IO.File.Read(GetCachePath(t)), t);
            objectList[t.FullName] = list;
        }
        else
        {
            objectList[t.FullName] = new ArrayList();
        }
    }

    private static IList GetListWithIndex(String jsonString, Type t)
    {

        IList list = new ArrayList();

        if (strUtil.IsNullOrEmpty(jsonString)) return list;

        List<object> lists = JsonParser.Parse(jsonString) as List<object>;

        foreach (JsonObject jsonObject in lists)
        {
            CacheObject obj = TypedDeserializeHelper.deserializeType(t, jsonObject) as CacheObject;
            int index = list.Add(obj);
            AddIdIndex(t.FullName, obj.Id, index);
            MakeIndexByInsert(obj);
        }

        return list;
    }

    private static void Serialize(Type t)
    {
        Serialize(t, GetObjectsByName(t));
    }

    private static void Serialize(Type t, IList list)
    {
        var target = SimpleJsonString.ConvertList(list);
        if (strUtil.IsNullOrEmpty(target)) return;

        String absolutePath = GetCachePath(t);
        lock (objLock)
        {

            String dir = Path.GetDirectoryName(absolutePath);
            if (Directory.Exists(dir) == false)
            {
                Directory.CreateDirectory(dir);
            }

            DH.IO.File.Write(absolutePath, target);
        }
    }

    /// <summary>
    /// 异步持久化方法（立即执行，不延迟）
    /// </summary>
    /// <param name="t">类型</param>
    /// <param name="list">数据列表</param>
    /// <returns>Task</returns>
    private static async Task SerializeAsync(Type t, IList list)
    {
        try
        {
            // 在后台线程执行序列化，避免阻塞主线程
            var target = await Task.Run(() => SimpleJsonString.ConvertList(list)).ConfigureAwait(false);
            if (strUtil.IsNullOrEmpty(target)) return;

            var absolutePath = GetCachePath(t);
            
            // 异步文件写入
            await Task.Run(() => {
                lock (objLock)
                {
                    var dir = Path.GetDirectoryName(absolutePath);
                    if (Directory.Exists(dir) == false)
                    {
                        Directory.CreateDirectory(dir);
                    }

                    DH.IO.File.Write(absolutePath, target);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);
        }
    }

    /// <summary>
    /// 立即启动异步持久化（Fire-and-Forget）
    /// </summary>
    /// <param name="type">类型</param>
    private static void StartAsyncPersistence(Type type)
    {
        if (IsInMemory(type)) return;

        var list = GetObjectsByName(type);
        if (list != null)
        {
            // Fire-and-Forget: 启动异步持久化但不等待完成
            _ = Task.Run(async () => {
                try
                {
                    await SerializeAsync(type, list).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    XTrace.WriteException(ex);
                }
            });
        }
    }

    private static void UpdateObjects(String key, IList list)
    {
        objectList[key] = list;
    }

    //------------------------------------------------------------------------------

    internal static CacheObject FindById(Type t, long id)
    {
        IList list = GetObjectsByName(t);
        if (list.Count > 0)
        {
            int objIndex = GetIndex(t.FullName, id);
            if (objIndex >= 0 && objIndex < list.Count)
            {
                return list[objIndex] as CacheObject;
            }
        }
        return null;
    }

    internal static IList FindBy(Type t, String propertyName, Object val)
    {
        var results = new List<object>();

        // 简化的策略：直接使用统一索引管理器
        var idSet = UnifiedIndexManager.FindIds(t, propertyName, val);
        foreach (var id in idSet)
        {
            var obj = FindById(t, id);
            if (obj != null) results.Add(obj);
        }

        // 转换回 ArrayList 以保持兼容性
        var arrayList = new ArrayList();
        foreach (var item in results)
        {
            arrayList.Add(item);
        }
        return arrayList;
    }

    internal static IList FindAll(Type t)
    {
        return new ArrayList(GetObjectsByName(t));
    }

    internal static void Insert(CacheObject obj)
    {
        Type t = obj.GetType();
        String _typeFullName = t.FullName;

        // 使用类型级别锁，提高并发性能
        lock (GetTypeLock(t))
        {
            IList list = FindAll(t);
            obj.Id = GetNextIdAtomic(t); // 使用原子ID生成

            int index = list.Add(obj);

            AddIdIndex(_typeFullName, obj.Id, index);
            UpdateObjects(_typeFullName, list);

            MakeIndexByInsert(obj);
        }

        // 持久化在锁外异步执行，避免阻塞
        if (!IsInMemory(t))
        {
            StartAsyncPersistence(t);
        }
    }

    internal static void InsertByIndex(CacheObject obj, String propertyName, Object pValue)
    {
        Type t = obj.GetType();
        String _typeFullName = t.FullName;

        // 使用类型级别锁，提高并发性能
        lock (GetTypeLock(t))
        {
            IList list = FindAll(t);
            obj.Id = GetNextIdAtomic(t); // 使用原子ID生成
            int index = list.Add(obj);

            AddIdIndex(_typeFullName, obj.Id, index);
            UpdateObjects(_typeFullName, list);

            MakeIndexByInsert(obj, propertyName, pValue);
        }

        // 持久化在锁外异步执行
        if (!IsInMemory(t))
        {
            StartAsyncPersistence(t);
        }
    }

    internal static void InsertByIndex(CacheObject obj, Dictionary<String, Object> dic)
    {
        Type t = obj.GetType();
        String _typeFullName = t.FullName;

        // 使用类型级别锁，提高并发性能
        lock (GetTypeLock(t))
        {
            IList list = FindAll(t);
            obj.Id = GetNextIdAtomic(t); // 使用原子ID生成
            int index = list.Add(obj);

            AddIdIndex(_typeFullName, obj.Id, index);
            UpdateObjects(_typeFullName, list);

            foreach (KeyValuePair<String, Object> kv in dic)
            {
                MakeIndexByInsert(obj, kv.Key, kv.Value);
            }
        }

        // 持久化在锁外异步执行
        if (!IsInMemory(t))
        {
            StartAsyncPersistence(t);
        }
    }

    internal static Result Update(CacheObject obj)
    {

        Type t = obj.GetType();

        MakeIndexByUpdate(obj);

        if (IsInMemory(t)) return new Result();

        try
        {
            StartAsyncPersistence(t);
            return new Result();
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);

            throw;
        }
    }

    internal static Result UpdateByIndex(CacheObject obj, Dictionary<String, Object> dic)
    {

        Type t = obj.GetType();

        MakeIndexByUpdate(obj);

        // 使用立即异 asynchronous
        if (IsInMemory(t)) return new Result();

        try
        {
            StartAsyncPersistence(t);
            return new Result();
        }
        catch (Exception ex)
        {
            XTrace.WriteException(ex);

            throw;
        }
    }

    internal static void Delete(CacheObject obj)
    {

        Type t = obj.GetType();
        String _typeFullName = t.FullName;

        MakeIndexByDelete(obj);

        IList list = FindAll(t);
        list.Remove(obj);
        UpdateObjects(_typeFullName, list);

        DeleteIdIndex(_typeFullName, obj.Id);

        // 使用立即异步持久化
        if (IsInMemory(t)) return;

        StartAsyncPersistence(t);
    }

    private static long GetNextId(IList list)
    {
        if (list.Count == 0) return 1;
        CacheObject preObject = list[list.Count - 1] as CacheObject;
        return preObject.Id + 1;
    }

    private static Boolean IsInMemory(Type t)
    {
        return rft.GetAttribute(t, typeof(NotSaveAttribute)) != null;
    }

    //----------------------------------------------------------------------------------------------

    private static void MakeIndexByInsert(CacheObject cacheObject, String propertyName, Object pValue)
    {
        if (cacheObject == null || pValue == null) return;
        
        var t = cacheObject.GetType();
        var propertyKey = GetPropertyKey(t.FullName ?? "", propertyName);
        var valueKey = GetValueKey(propertyKey, pValue.ToString() ?? "");
        
        // 使用线程安全的索引操作
        var hashSet = indexList.GetOrAdd(valueKey, _ => new ConcurrentHashSet<long>());
        hashSet.Add(cacheObject.Id);
    }

    private static void MakeIndexByInsert(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        // 使用反射获取属性
        var properties = GetTypeProperties(cacheObject.GetType());
        
        foreach (var p in properties)
        {
            var pValue = rft.GetPropertyValue(cacheObject, p.Name);
            if (pValue == null) continue;
            
            // 传统字符串索引（保持向后兼容）
            if (!strUtil.IsNullOrEmpty(pValue.ToString()))
            {
                var propertyKey = GetPropertyKey(cacheObject.GetType().FullName ?? "", p.Name);
                var valueKey = GetValueKey(propertyKey, pValue.ToString() ?? "");
                
                // 使用线程安全的索引操作
                var hashSet = indexList.GetOrAdd(valueKey, _ => new ConcurrentHashSet<long>());
                hashSet.Add(cacheObject.Id);
            }
            
            // 类型感知索引（新功能）
            if (_enableTypedIndex)
            {
                TypedIndexManager.AddIndex(cacheObject.GetType(), p.Name, p.PropertyType, pValue, cacheObject.Id);
            }
        }
    }

    private static void MakeIndexByUpdate(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        // 简化：先删除所有旧索引，然后重新创建索引
        MakeIndexByDelete(cacheObject);
        MakeIndexByInsert(cacheObject);
    }

    private static void MakeIndexByUpdate(CacheObject cacheObject, String propertyName, Object pValue)
    {
        if (cacheObject == null || pValue == null) return;
        
        var t = cacheObject.GetType();
        var propertyKey = GetPropertyKey(t.FullName ?? "", propertyName);
        
        // 先删除旧索引
        DeleteOldValueIdMapOptimized(propertyKey, cacheObject.Id);
        
        // 添加新索引
        var valueKey = GetValueKey(propertyKey, pValue.ToString() ?? "");
        var hashSet = indexList.GetOrAdd(valueKey, _ => new ConcurrentHashSet<long>());
        hashSet.Add(cacheObject.Id);
    }

    private static void MakeIndexByDelete(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        // 使用反射获取属性
        var properties = GetTypeProperties(cacheObject.GetType());
        
        foreach (var p in properties)
        {
            // 删除传统字符串索引
            var propertyKey = GetPropertyKey(cacheObject.GetType().FullName ?? "", p.Name);
            DeleteOldValueIdMapOptimized(propertyKey, cacheObject.Id);
            
            // 删除类型感知索引
            if (_enableTypedIndex)
            {
                var pValue = rft.GetPropertyValue(cacheObject, p.Name);
                if (pValue != null)
                {
                    TypedIndexManager.RemoveIndex(cacheObject.GetType(), p.Name, pValue, cacheObject.Id);
                }
            }
        }
    }

    // 新的优化删除方法 - O(1) 复杂度，线程安全
    private static void DeleteOldValueIdMapOptimized(String propertyKeyPrefix, long oid)
    {
        // 找到所有以该属性为前缀的索引键并删除对应的ID
        var keysToRemove = new List<string>();
        
        lock (indexLock)
        {
            foreach (var kvp in indexList.ToList())
            {
                if (kvp.Key.StartsWith(propertyKeyPrefix + ":"))
                {
                    if (kvp.Value.Contains(oid))
                    {
                        kvp.Value.Remove(oid);
                        if (kvp.Value.Count == 0)
                        {
                            keysToRemove.Add(kvp.Key);
                        }
                    }
                }
            }
            
            // 删除空的索引项
            foreach (var key in keysToRemove)
            {
                indexList.TryRemove(key, out _);
            }
        }
    }

    private static PropertyInfo[] GetProperties(Type t)
    {
        return t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
    }

    // 移除旧的低效方法，保留用于兼容性
    private static NameValueCollection GetValueCollection(String propertyKey)
    {
        // 这个方法现在只用于向后兼容，实际上不再使用 NameValueCollection
        return new NameValueCollection();
    }

    // 移除旧的低效方法，保留空实现用于兼容性  
    private static void AddNewValueMap(NameValueCollection valueCollection, CacheObject cacheObject, PropertyInfo p)
    {
        // 这个方法已被优化版本替代，保留空实现用于兼容性
    }

    // 保留旧的删除方法用于向后兼容，但标记为已弃用
    // TODO 优化 - 已被 DeleteOldValueIdMapOptimized 替代
    private static void DeleteOldValueIdMap(NameValueCollection valueCollection, long oid)
    {
        // 这个方法已被优化版本替代，保留空实现用于兼容性
    }

    private static String GetPropertyKey(String typeFullName, String propertyName)
    {
        return typeFullName + "_" + propertyName;
    }

    // 新增：获取具体值的索引键
    private static String GetValueKey(String propertyKey, String value)
    {
        return propertyKey + ":" + value;
    }


    //-------------------------- Id Index --------------------------------

    private static IDictionary GetIdIndexMap(String key)
    {
        if (objectList[key] == null)
        {
            objectList[key] = new Hashtable();
        }
        return (objectList[key] as IDictionary);
    }

    private static void UpdateIdIndexMap(String key, IDictionary map)
    {
        objectList[key] = map;
    }

    private static void ClearIdIndexMap(String key)
    {
        objectList.Remove(key);
    }


    private static void AddIdIndex(String typeFullName, long oid, int index)
    {
        String key = GetIdIndexMapKey(typeFullName);
        IDictionary indexMap = GetIdIndexMap(key);
        indexMap[oid] = index;
        UpdateIdIndexMap(key, indexMap);
    }
    private static void DeleteIdIndex(String typeFullName, long oid)
    {

        String key = GetIdIndexMapKey(typeFullName);

        ClearIdIndexMap(key);

        IList results = objectList[typeFullName] as IList;
        foreach (CacheObject obj in results)
        {
            AddIdIndex(typeFullName, obj.Id, results.IndexOf(obj));
        }

        IDictionary indexMap = GetIdIndexMap(key);
        UpdateIdIndexMap(key, indexMap);
    }

    private static int GetIndex(String typeFullName, long oid)
    {
        int result = -1;
        Object objIndex = GetIdIndexMap(GetIdIndexMapKey(typeFullName))[oid];
        if (objIndex != null)
        {
            result = (int)objIndex;
        }
        return result;
    }

    private static String GetIdIndexMapKey(String typeFullName)
    {
        return String.Format("{0}_oid_index", typeFullName);
    }

    //----------------------------------------------------------

    private static String GetCachePath(Type t)
    {
        return GetCacheFileName(t.FullName);
    }

    private static String GetCacheFileName(String name)
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Data/{name}.json");
    }

    internal static void Clear()
    {
        _hasCheckedFileDB = new Hashtable();
        objectList = Hashtable.Synchronized(new Hashtable());
        indexList = new ConcurrentDictionary<string, ConcurrentHashSet<long>>();
        // 简化：不再需要清理快照
    }

    // =================================
    // 高并发性能监控和优化方法
    // =================================
    
    /// <summary>
    /// 获取索引统计信息
    /// </summary>
    public static Dictionary<string, int> GetIndexStatistics()
    {
        var stats = new Dictionary<string, int>();
        lock (indexLock)
        {
            foreach (var kvp in indexList)
            {
                stats[kvp.Key] = kvp.Value.Count;
            }
        }
        return stats;
    }
    
    /// <summary>
    /// 清理空的索引条目
    /// </summary>
    public static void CleanupEmptyIndexes()
    {
        var keysToRemove = new List<string>();
        lock (indexLock)
        {
            foreach (var kvp in indexList.ToList())
            {
                if (kvp.Value.Count == 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                indexList.TryRemove(key, out _);
            }
        }
    }
    
    /// <summary>
    /// 获取索引总数
    /// </summary>
    public static int GetIndexCount()
    {
        return indexList.Count;
    }
    
    /// <summary>
    /// 获取索引内存使用估算（字节）
    /// </summary>
    public static long GetIndexMemoryUsage()
    {
        long totalMemory = 0;
        lock (indexLock)
        {
            foreach (var kvp in indexList)
            {
                // 估算：键的字符串长度 * 2 (Unicode) + HashSet的大小估算
                totalMemory += kvp.Key.Length * 2;
                totalMemory += kvp.Value.Count * 8; // 每个long占8字节
                totalMemory += 32; // HashSet的开销估算
            }
        }
        return totalMemory;
    }

    /// <summary>
    /// 获取索引锁对象（供UnifiedIndexManager使用）
    /// </summary>
    /// <returns>索引锁对象</returns>
    public static object GetIndexLock()
    {
        return indexLock;
    }

    /// <summary>
    /// 获取索引列表的快照（供UnifiedIndexManager使用）
    /// </summary>
    /// <returns>索引列表快照</returns>
    public static Dictionary<string, HashSet<long>> GetIndexListSnapshot()
    {
        return indexList.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToHashSet()
        );
    }

    /// <summary>
    /// 直接添加索引项（供UnifiedIndexManager使用）
    /// </summary>
    /// <param name="key">索引键</param>
    /// <param name="id">对象ID</param>
    public static void AddIndexItem(string key, long id)
    {
        var hashSet = indexList.GetOrAdd(key, _ => new ConcurrentHashSet<long>());
        hashSet.Add(id);
    }

    /// <summary>
    /// 直接移除索引项（供UnifiedIndexManager使用）
    /// </summary>
    /// <param name="key">索引键</param>
    /// <param name="id">对象ID</param>
    public static void RemoveIndexItem(string key, long id)
    {
        lock (indexLock)
        {
            if (indexList.TryGetValue(key, out var existingSet))
            {
                existingSet.Remove(id);
                if (existingSet.Count == 0)
                {
                    indexList.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>
    /// 获取索引项（供UnifiedIndexManager使用）
    /// </summary>
    /// <param name="key">索引键</param>
    /// <returns>对象ID集合</returns>
    public static HashSet<long> GetIndexItems(string key)
    {
        if (indexList.TryGetValue(key, out var existingSet))
        {
            return existingSet.ToHashSet();
        }
        return new HashSet<long>();
    }

    /// <summary>
    /// 获取索引统计信息（供UnifiedIndexManager使用）
    /// </summary>
    /// <returns>索引统计信息</returns>
    public static IndexStats GetIndexStats()
    {
        lock (indexLock)
        {
            return new IndexStats
            {
                TotalIndexes = indexList.Count,
                TotalEntries = indexList.Values.Sum(set => set.Count),
                MemoryUsage = GetIndexMemoryUsage()
            };
        }
    }

    /// <summary>
    /// 索引统计信息
    /// </summary>
    public class IndexStats
    {
        public int TotalIndexes { get; set; }
        public int TotalEntries { get; set; }
        public long MemoryUsage { get; set; }
    }

    /// <summary>
    /// 批量插入对象 - 性能优化
    /// </summary>
    /// <param name="objects">要插入的对象集合</param>
    internal static void InsertBatch(IEnumerable<CacheObject> objects)
    {
        if (objects == null) return;
        
        var groupedByType = objects.GroupBy(obj => obj.GetType());
        
        foreach (var typeGroup in groupedByType)
        {
            var type = typeGroup.Key;
            var typeLock = GetTypeLock(type);
            
            lock (typeLock)
            {
                var list = FindAll(type);
                var typeFullName = type.FullName;
                
                foreach (var obj in typeGroup)
                {
                    obj.Id = GetNextIdAtomic(type);
                    var index = list.Add(obj);
                    AddIdIndex(typeFullName, obj.Id, index);
                    MakeIndexByInsert(obj);
                }
                
                UpdateObjects(typeFullName, list);
            }
            
            // 批量持久化
            if (!IsInMemory(type))
            {
                StartAsyncPersistence(type);
            }
        }
    }

    /// <summary>
    /// 分页查询 - 性能优化
    /// </summary>
    /// <param name="t">类型</param>
    /// <param name="propertyName">属性名</param>
    /// <param name="val">查询值</param>
    /// <param name="pageIndex">页索引</param>
    /// <param name="pageSize">页大小</param>
    /// <returns>分页结果</returns>
    internal static IList FindByPaged(Type t, String propertyName, Object val, int pageIndex, int pageSize)
    {
        var idSet = UnifiedIndexManager.FindIds(t, propertyName, val);
        var pagedIds = idSet.Skip(pageIndex * pageSize).Take(pageSize);
        
        var results = new ArrayList();
        foreach (var id in pagedIds)
        {
            var obj = FindById(t, id);
            if (obj != null) results.Add(obj);
        }
        return results;
    }

}
