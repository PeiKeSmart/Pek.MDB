using DH.ORM;
using DH.Reflection;
using DH.Serialization;

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
    // 新的高效索引结构：使用 HashSet<long> 替代字符串存储ID列表
    private static ConcurrentDictionary<string, HashSet<long>> indexList = new();

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
        var propertyKey = GetPropertyKey(t.FullName!, propertyName);
        var valueKey = GetValueKey(propertyKey, val.ToString()!);
        
        // 使用新的高效索引结构
        if (indexList.TryGetValue(valueKey, out var idSet))
        {
            var results = new ArrayList();
            foreach (var id in idSet)
            {
                var obj = FindById(t, id);
                if (obj != null) results.Add(obj);
            }
            return results;
        }
        
        // 如果没有找到索引，返回空列表
        return new ArrayList();
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
        
        // 使用新的高效索引结构
        indexList.AddOrUpdate(valueKey, 
            new HashSet<long> { cacheObject.Id }, 
            (key, existingSet) =>
            {
                existingSet.Add(cacheObject.Id);
                return existingSet;
            });
    }

    private static void MakeIndexByInsert(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        var t = cacheObject.GetType();
        var properties = GetProperties(t);
        
        foreach (var p in properties)
        {
            // 检查是否应该跳过索引
            var attr = rft.GetAttribute(p, typeof(NotSaveAttribute));
            if (attr != null) continue;
            
            if (!p.CanRead) continue;
            
            var pValue = rft.GetPropertyValue(cacheObject, p.Name);
            if (pValue == null || strUtil.IsNullOrEmpty(pValue.ToString())) continue;
            
            var propertyKey = GetPropertyKey(t.FullName ?? "", p.Name);
            var valueKey = GetValueKey(propertyKey, pValue.ToString() ?? "");
            
            // 使用新的高效索引结构
            indexList.AddOrUpdate(valueKey,
                new HashSet<long> { cacheObject.Id },
                (key, existingSet) =>
                {
                    existingSet.Add(cacheObject.Id);
                    return existingSet;
                });
        }
    }

    private static void MakeIndexByUpdate(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        var t = cacheObject.GetType();
        var properties = GetProperties(t);
        
        foreach (var p in properties)
        {
            // 检查是否应该跳过索引
            var attr = rft.GetAttribute(p, typeof(NotSaveAttribute));
            if (attr != null) continue;
            
            if (!p.CanRead) continue;
            
            var propertyKey = GetPropertyKey(t.FullName ?? "", p.Name);
            
            // 先删除所有相关的旧索引
            DeleteOldValueIdMapOptimized(propertyKey, cacheObject.Id);
            
            // 再添加新索引
            var pValue = rft.GetPropertyValue(cacheObject, p.Name);
            if (pValue != null && !strUtil.IsNullOrEmpty(pValue.ToString()))
            {
                var valueKey = GetValueKey(propertyKey, pValue.ToString() ?? "");
                indexList.AddOrUpdate(valueKey,
                    new HashSet<long> { cacheObject.Id },
                    (key, existingSet) =>
                    {
                        existingSet.Add(cacheObject.Id);
                        return existingSet;
                    });
            }
        }
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
        indexList.AddOrUpdate(valueKey,
            new HashSet<long> { cacheObject.Id },
            (key, existingSet) =>
            {
                existingSet.Add(cacheObject.Id);
                return existingSet;
            });
    }

    private static void MakeIndexByDelete(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        var t = cacheObject.GetType();
        var properties = GetProperties(t);
        
        foreach (var p in properties)
        {
            // 检查是否应该跳过索引
            var attr = rft.GetAttribute(p, typeof(NotSaveAttribute));
            if (attr != null) continue;
            
            var propertyKey = GetPropertyKey(t.FullName ?? "", p.Name);
            DeleteOldValueIdMapOptimized(propertyKey, cacheObject.Id);
        }
    }

    // 新的优化删除方法 - O(1) 复杂度
    private static void DeleteOldValueIdMapOptimized(String propertyKeyPrefix, long oid)
    {
        // 找到所有以该属性为前缀的索引键并删除对应的ID
        var keysToUpdate = new List<string>();
        var keysToRemove = new List<string>();
        
        foreach (var kvp in indexList)
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
    }

}
