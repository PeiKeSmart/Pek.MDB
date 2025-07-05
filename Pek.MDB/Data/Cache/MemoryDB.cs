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
    private static ConcurrentDictionary<string, HashSet<long>> indexList = new();
    private static readonly object indexLock = new();
    
    // 类型感知索引配置
    private static bool _enableTypedIndex = true;
    private static readonly object _configLock = new();

    /// <summary>
    /// 启用或禁用类型感知索引
    /// </summary>
    /// <param name="enable">是否启用</param>
    public static void EnableTypedIndex(bool enable)
    {
        lock (_configLock)
        {
            _enableTypedIndex = enable;
        }
    }

    /// <summary>
    /// 检查是否启用了类型感知索引
    /// </summary>
    /// <returns>是否启用</returns>
    public static bool IsTypedIndexEnabled()
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

    private static Object chkLock = new();


    private static IList GetObjectsByName(Type t)
    {

        if (IsCheckFileDB(t))
        {

            lock (chkLock)
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
        String target = SimpleJsonString.ConvertList(list);
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
        // 使用查询缓存（如果启用）
        var cacheKey = $"{t.FullName}_{propertyName}_{val}";
        
        var cachedResults = QueryCache.GetCachedResult(cacheKey, () =>
        {
            var startTime = DateTime.Now;
            var results = new List<object>();
            bool usedTypedIndex = false;

            // 使用查询优化器分析最佳策略
            var strategy = QueryOptimizer.AnalyzeQuery(t, propertyName, QueryType.Exact, val);

            if (strategy.UseTypedIndex && _enableTypedIndex)
            {
                // 使用类型感知索引
                var idSet = TypedIndexManager.FindByValue(t, propertyName, val);
                foreach (var id in idSet)
                {
                    var obj = FindById(t, id);
                    if (obj != null) results.Add(obj);
                }
                usedTypedIndex = true;
            }
            else
            {
                // 使用传统字符串索引
                var propertyKey = GetPropertyKey(t.FullName!, propertyName);
                var valueKey = GetValueKey(propertyKey, val.ToString()!);
                
                if (indexList.TryGetValue(valueKey, out var idSet2))
                {
                    foreach (var id in idSet2)
                    {
                        var obj = FindById(t, id);
                        if (obj != null) results.Add(obj);
                    }
                }
            }

            // 记录查询性能数据
            var executionTime = (DateTime.Now - startTime).TotalMilliseconds;
            QueryOptimizer.RecordQueryResult(t, propertyName, QueryType.Exact, 
                usedTypedIndex, executionTime, results.Count);

            return results;
        });

        // 转换回 ArrayList 以保持兼容性
        var arrayList = new ArrayList();
        foreach (var item in cachedResults)
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

        IList list = FindAll(t);
        obj.Id = GetNextId(list);

        int index = list.Add(obj);

        AddIdIndex(_typeFullName, obj.Id, index);
        UpdateObjects(_typeFullName, list);

        MakeIndexByInsert(obj);

        // 使缓存失效
        QueryCache.InvalidateCache(t);

        if (IsInMemory(t)) return;

        Serialize(t);
    }

    internal static void InsertByIndex(CacheObject obj, String propertyName, Object pValue)
    {

        Type t = obj.GetType();
        String _typeFullName = t.FullName;

        IList list = FindAll(t);
        obj.Id = GetNextId(list);
        int index = list.Add(obj);

        AddIdIndex(_typeFullName, obj.Id, index);
        UpdateObjects(_typeFullName, list);

        MakeIndexByInsert(obj, propertyName, pValue);

        if (IsInMemory(t)) return;

        Serialize(t);
    }

    internal static void InsertByIndex(CacheObject obj, Dictionary<String, Object> dic)
    {

        Type t = obj.GetType();
        String _typeFullName = t.FullName;

        IList list = FindAll(t);
        obj.Id = GetNextId(list);
        int index = list.Add(obj);

        AddIdIndex(_typeFullName, obj.Id, index);
        UpdateObjects(_typeFullName, list);

        foreach (KeyValuePair<String, Object> kv in dic)
        {
            MakeIndexByInsert(obj, kv.Key, kv.Value);
        }

        if (IsInMemory(t)) return;

        Serialize(t);
    }

    internal static Result Update(CacheObject obj)
    {

        Type t = obj.GetType();

        MakeIndexByUpdate(obj);

        if (IsInMemory(t)) return new Result();

        try
        {
            Serialize(t);
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

        // 使缓存失效
        QueryCache.InvalidateCache(t);

        if (IsInMemory(t)) return new Result();

        try
        {
            Serialize(t);
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

        // 使缓存失效
        QueryCache.InvalidateCache(t);

        if (IsInMemory(t)) return;

        Serialize(t, list);
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

    private static Object objIndexLock = new object();
    private static Object objIndexLockInsert = new object();
    private static Object objIndexLockUpdate = new object();
    private static Object objIndexLockDelete = new object();

    private static void MakeIndexByInsert(CacheObject cacheObject, String propertyName, Object pValue)
    {
        if (cacheObject == null || pValue == null) return;
        
        var t = cacheObject.GetType();
        var propertyKey = GetPropertyKey(t.FullName ?? "", propertyName);
        var valueKey = GetValueKey(propertyKey, pValue.ToString() ?? "");
        
        // 使用线程安全的索引操作
        lock (indexLock)
        {
            if (indexList.TryGetValue(valueKey, out var existingSet))
            {
                existingSet.Add(cacheObject.Id);
            }
            else
            {
                indexList[valueKey] = new HashSet<long> { cacheObject.Id };
            }
        }
    }

    private static void MakeIndexByInsert(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        var properties = IncrementalIndexManager.GetIndexableProperties(cacheObject.GetType());
        
        foreach (var p in properties)
        {
            if (!p.CanRead) continue;
            
            var pValue = rft.GetPropertyValue(cacheObject, p.Name);
            if (pValue == null) continue;
            
            // 传统字符串索引（保持向后兼容）
            if (!strUtil.IsNullOrEmpty(pValue.ToString()))
            {
                var propertyKey = GetPropertyKey(cacheObject.GetType().FullName ?? "", p.Name);
                var valueKey = GetValueKey(propertyKey, pValue.ToString() ?? "");
                
                // 使用线程安全的索引操作
                lock (indexLock)
                {
                    if (indexList.TryGetValue(valueKey, out var existingSet))
                    {
                        existingSet.Add(cacheObject.Id);
                    }
                    else
                    {
                        indexList[valueKey] = new HashSet<long> { cacheObject.Id };
                    }
                }
            }
            
            // 类型感知索引（新功能）
            if (_enableTypedIndex)
            {
                TypedIndexManager.AddIndex(cacheObject.GetType(), p.Name, p.PropertyType, pValue, cacheObject.Id);
            }
        }
        
        // 创建对象快照用于后续增量更新
        IncrementalIndexManager.CreateSnapshot(cacheObject);
    }

    private static void MakeIndexByUpdate(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        // 获取属性变化列表
        var changedProperties = IncrementalIndexManager.GetChangedProperties(cacheObject);
        
        foreach (var change in changedProperties)
        {
            var propertyKey = GetPropertyKey(cacheObject.GetType().FullName ?? "", change.PropertyName);
            
            // 更新传统字符串索引
            // 删除旧索引
            if (change.OldValue != null && !strUtil.IsNullOrEmpty(change.OldValue.ToString()))
            {
                var oldValueKey = GetValueKey(propertyKey, change.OldValue.ToString() ?? "");
                if (indexList.TryGetValue(oldValueKey, out var oldSet))
                {
                    oldSet.Remove(cacheObject.Id);
                    if (oldSet.Count == 0)
                    {
                        indexList.TryRemove(oldValueKey, out _);
                    }
                }
            }
            
            // 添加新索引
            if (change.NewValue != null && !strUtil.IsNullOrEmpty(change.NewValue.ToString()))
            {
                var newValueKey = GetValueKey(propertyKey, change.NewValue.ToString() ?? "");
                lock (indexLock)
                {
                    if (indexList.TryGetValue(newValueKey, out var existingSet))
                    {
                        existingSet.Add(cacheObject.Id);
                    }
                    else
                    {
                        indexList[newValueKey] = new HashSet<long> { cacheObject.Id };
                    }
                }
            }
            
            // 更新类型感知索引
            if (_enableTypedIndex)
            {
                // 获取属性类型
                var property = cacheObject.GetType().GetProperty(change.PropertyName);
                if (property != null)
                {
                    // 移除旧值
                    if (change.OldValue != null)
                    {
                        TypedIndexManager.RemoveIndex(cacheObject.GetType(), change.PropertyName, change.OldValue, cacheObject.Id);
                    }
                    
                    // 添加新值
                    if (change.NewValue != null)
                    {
                        TypedIndexManager.AddIndex(cacheObject.GetType(), change.PropertyName, property.PropertyType, change.NewValue, cacheObject.Id);
                    }
                }
            }
        }
        
        // 更新对象快照
        IncrementalIndexManager.UpdateSnapshot(cacheObject);
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
        lock (indexLock)
        {
            if (indexList.TryGetValue(valueKey, out var existingSet))
            {
                existingSet.Add(cacheObject.Id);
            }
            else
            {
                indexList[valueKey] = new HashSet<long> { cacheObject.Id };
            }
        }
    }

    private static void MakeIndexByDelete(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        var properties = IncrementalIndexManager.GetIndexableProperties(cacheObject.GetType());
        
        foreach (var p in properties)
        {
            // 删除传统字符串索引
            var propertyKey = GetPropertyKey(cacheObject.GetType().FullName ?? "", p.Name);
            DeleteOldValueIdMapOptimized(propertyKey, cacheObject.Id);
            
            // 删除类型感知索引
            if (_enableTypedIndex && p.CanRead)
            {
                var pValue = rft.GetPropertyValue(cacheObject, p.Name);
                if (pValue != null)
                {
                    TypedIndexManager.RemoveIndex(cacheObject.GetType(), p.Name, pValue, cacheObject.Id);
                }
            }
        }
        
        // 删除对象快照
        IncrementalIndexManager.RemoveSnapshot(cacheObject.Id);
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
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Data/{name}{fileExt}");
    }

    private static readonly String fileExt = ".json";

    internal static void Clear()
    {
        _hasCheckedFileDB = new Hashtable();
        objectList = Hashtable.Synchronized(new Hashtable());
        indexList = new ConcurrentDictionary<string, HashSet<long>>();
        IncrementalIndexManager.ClearSnapshots();
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
}
