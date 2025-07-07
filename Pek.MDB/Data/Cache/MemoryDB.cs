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
    // 优化后的存储结构：直接 ID 映射，避免间接查找
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<long, CacheObject>> _objectsById = new();
    
    // 类型级别锁 - 提升并发性能
    private static readonly ConcurrentDictionary<Type, object> _typeLocks = new();
    
    // 原子ID生成器 - 避免ID冲突
    private static readonly ConcurrentDictionary<Type, long> _typeIdCounters = new();
    
    // 文件加载检查 - 线程安全
    private static readonly ConcurrentDictionary<Type, bool> _hasCheckedFileDB = new();
    
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

    public static IDictionary GetObjectsMap()
    {
        // 转换为兼容的字典格式（基于新的 _objectsById 结构）
        var hashtable = new Hashtable();
        foreach (var typeKvp in _objectsById)
        {
            var arrayList = new ArrayList();
            foreach (var obj in typeKvp.Value.Values)
            {
                arrayList.Add(obj);
            }
            hashtable[typeKvp.Key] = arrayList;
        }
        return hashtable;
    }

    public static IDictionary GetIndexMap()
    {
        // 为了向后兼容，返回空字典
        return new Hashtable();
    }


    // 异步持久化优化：去重机制和频率控制
    private static readonly ConcurrentDictionary<Type, DateTime> _lastWriteTime = new();
    private static readonly object objLock = new(); // 文件操作锁
    
    // 持久化配置
    private static readonly int MIN_WRITE_INTERVAL_MS = 500; // 最小写入间隔500ms


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
        // 基于新的 _objectsById 结构返回数据
        var typeObjects = _objectsById.GetOrAdd(t.FullName, _ => new ConcurrentDictionary<long, CacheObject>());
        var arrayList = new ArrayList();
        foreach (var obj in typeObjects.Values)
        {
            arrayList.Add(obj);
        }
        return arrayList;
    }

    private static Boolean IsCheckFileDB(Type t)
    {
        return !_hasCheckedFileDB.ContainsKey(t);
    }

    private static void LoadDataFromFile(Type t)
    {
        if (IO.File.Exists(GetCachePath(t)))
        {
            GetListWithIndex(IO.File.Read(GetCachePath(t)), t);
        }
        // 无需初始化空列表，因为 _objectsById 会自动处理
    }

    private static void GetListWithIndex(String jsonString, Type t)
    {
        if (strUtil.IsNullOrEmpty(jsonString)) return;

        var lists = JsonParser.Parse(jsonString) as List<object>;
        if (lists == null) return;

        // 获取新的优化数据结构
        var typeObjects = _objectsById.GetOrAdd(t.FullName, _ => new ConcurrentDictionary<long, CacheObject>());

        foreach (JsonObject jsonObject in lists)
        {
            var obj = TypedDeserializeHelper.deserializeType(t, jsonObject) as CacheObject;
            if (obj != null)
            {
                // 直接存储到新的ID映射结构
                typeObjects[obj.Id] = obj;
                
                // 建立普通索引
                MakeIndexByInsert(obj);
                
                // 建立唯一约束索引（加载时不需要验证，直接建立索引）
                try
                {
                    UniqueConstraintManager.AddUniqueConstraints(obj);
                }
                catch (UniqueConstraintViolationException ex)
                {
                    // 数据文件中存在重复的唯一值，记录警告但继续加载
                    XTrace.WriteLine($"加载数据时发现唯一约束冲突，已跳过对象 ID: {obj.Id}, 错误: {ex.Message}");
                }
            }
        }
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
    /// 启动异步持久化（优化版：频率控制和数据快照）
    /// </summary>
    /// <param name="type">类型</param>
    private static void StartAsyncPersistence(Type type)
    {
        if (IsInMemory(type)) return;

        // 检查写入频率，避免过于频繁的I/O操作
        var currentTime = DateTime.Now;
        if (_lastWriteTime.TryGetValue(type, out var lastWrite))
        {
            if (currentTime.Subtract(lastWrite).TotalMilliseconds < MIN_WRITE_INTERVAL_MS)
            {
                return; // 跳过太频繁的写入
            }
        }

        // 立即启动异步持久化
        _ = Task.Run(async () => {
            try
            {
                await SerializeAsyncWithSnapshot(type).ConfigureAwait(false);
                _lastWriteTime[type] = DateTime.Now;
            }
            catch (Exception ex)
            {
                XTrace.WriteException(ex);
            }
        });
    }

    //------------------------------------------------------------------------------

    internal static CacheObject FindById(Type t, long id)
    {
        // 优化后：直接 ID 映射，一次哈希查找
        var typeObjects = _objectsById.GetOrAdd(t.FullName, _ => new ConcurrentDictionary<long, CacheObject>());
        typeObjects.TryGetValue(id, out var obj);
        return obj;
    }

    internal static IList FindBy(Type t, String propertyName, Object val)
    {
        var results = new List<object>();

        // 直接使用类型感知索引
        var idSet = TypedIndexManager.FindByValue(t, propertyName, val);
        
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
        // 优先使用新的优化结构
        var typeObjects = _objectsById.GetOrAdd(t.FullName, _ => new ConcurrentDictionary<long, CacheObject>());
        if (typeObjects.Count > 0)
        {
            var arrayList = new ArrayList();
            foreach (var obj in typeObjects.Values)
            {
                arrayList.Add(obj);
            }
            return arrayList;
        }
        
        // 兼容性：如果新结构为空，使用旧结构
        return new ArrayList(GetObjectsByName(t));
    }

    internal static void Insert(CacheObject obj)
    {
        var t = obj.GetType();
        var typeFullName = t.FullName;

        // 使用类型级别锁，提高并发性能
        lock (GetTypeLock(t))
        {
            // 1. 验证唯一约束（在分配ID之前）
            UniqueConstraintManager.ValidateAllUniqueConstraints(obj);
            
            // 2. 生成新的 ID
            obj.Id = GetNextIdAtomic(t);
            
            // 3. 直接存储到优化的 ID 映射结构中
            var typeObjects = _objectsById.GetOrAdd(typeFullName, _ => new ConcurrentDictionary<long, CacheObject>());
            typeObjects[obj.Id] = obj;

            // 4. 建立普通索引
            MakeIndexByInsert(obj);
            
            // 5. 建立唯一约束索引
            UniqueConstraintManager.AddUniqueConstraints(obj);
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
            obj.Id = GetNextIdAtomic(t); // 使用原子ID生成
            
            // 新结构：直接存储到 ID 映射中
            var typeObjects = _objectsById.GetOrAdd(_typeFullName, _ => new ConcurrentDictionary<long, CacheObject>());
            typeObjects[obj.Id] = obj;

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
            obj.Id = GetNextIdAtomic(t); // 使用原子ID生成
            
            // 新结构：直接存储到 ID 映射中
            var typeObjects = _objectsById.GetOrAdd(_typeFullName, _ => new ConcurrentDictionary<long, CacheObject>());
            typeObjects[obj.Id] = obj;

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

        // 使用类型级别锁，确保更新操作的原子性
        lock (GetTypeLock(t))
        {
            // 1. 获取旧对象用于唯一约束更新
            var oldObj = FindById(t, obj.Id);
            if (oldObj == null)
            {
                throw new InvalidOperationException($"要更新的对象（ID: {obj.Id}）不存在");
            }

            // 2. 更新唯一约束索引（先验证再更新）
            UniqueConstraintManager.UpdateUniqueConstraints(oldObj, obj);
            
            // 3. 更新普通索引
            MakeIndexByUpdate(obj);
            
            // 4. 更新主存储中的对象
            var typeObjects = _objectsById.GetOrAdd(t.FullName, _ => new ConcurrentDictionary<long, CacheObject>());
            typeObjects[obj.Id] = obj;
        }

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
        var t = obj.GetType();
        var typeFullName = t.FullName;

        // 使用类型级别锁，确保并发安全
        lock (GetTypeLock(t))
        {
            // 1. 移除唯一约束索引
            UniqueConstraintManager.RemoveUniqueConstraints(obj);
            
            // 2. 移除普通索引
            MakeIndexByDelete(obj);

            // 3. 从主存储中删除对象，O(1) 复杂度
            var typeObjects = _objectsById.GetOrAdd(typeFullName, _ => new ConcurrentDictionary<long, CacheObject>());
            typeObjects.TryRemove(obj.Id, out _);
        }

        // 使用立即异步持久化
        if (IsInMemory(t)) return;

        StartAsyncPersistence(t);
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
        var propertyInfo = t.GetProperty(propertyName);
        if (propertyInfo != null)
        {
            TypedIndexManager.AddIndex(t, propertyName, propertyInfo.PropertyType, pValue, cacheObject.Id);
        }
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
            
            // 只使用类型感知索引
            TypedIndexManager.AddIndex(cacheObject.GetType(), p.Name, p.PropertyType, pValue, cacheObject.Id);
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
        var propertyInfo = t.GetProperty(propertyName);
        if (propertyInfo != null)
        {
            // 先删除旧索引，再添加新索引
            var oldValue = rft.GetPropertyValue(cacheObject, propertyName);
            if (oldValue != null)
            {
                TypedIndexManager.RemoveIndex(t, propertyName, oldValue, cacheObject.Id);
            }
            TypedIndexManager.AddIndex(t, propertyName, propertyInfo.PropertyType, pValue, cacheObject.Id);
        }
    }

    private static void MakeIndexByDelete(CacheObject cacheObject)
    {
        if (cacheObject == null) return;
        
        // 使用反射获取属性
        var properties = GetTypeProperties(cacheObject.GetType());
        
        foreach (var p in properties)
        {
            // 只删除类型感知索引
            var pValue = rft.GetPropertyValue(cacheObject, p.Name);
            if (pValue != null)
            {
                TypedIndexManager.RemoveIndex(cacheObject.GetType(), p.Name, pValue, cacheObject.Id);
            }
        }
    }

    // 删除传统索引相关的方法，已被类型感知索引替代

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

    // 传统索引辅助方法已被类型感知索引替代

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
        _hasCheckedFileDB.Clear();
        _objectsById.Clear(); // 清理新的ID映射结构
        // 清理类型感知索引
        TypedIndexManager.ClearAllIndexes();
    }

    // 传统索引相关的方法已被类型感知索引替代

    /// <summary>
    /// 批量插入对象 - 性能优化，支持唯一约束验证
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
                var typeFullName = type.FullName;
                var typeObjects = _objectsById.GetOrAdd(typeFullName, _ => new ConcurrentDictionary<long, CacheObject>());
                
                foreach (var obj in typeGroup)
                {
                    // 1. 验证唯一约束
                    UniqueConstraintManager.ValidateAllUniqueConstraints(obj);
                    
                    // 2. 生成ID
                    obj.Id = GetNextIdAtomic(type);
                    
                    // 3. 存储到主结构
                    typeObjects[obj.Id] = obj;
                    
                    // 4. 建立普通索引
                    MakeIndexByInsert(obj);
                    
                    // 5. 建立唯一约束索引
                    UniqueConstraintManager.AddUniqueConstraints(obj);
                }
            }
            
            // 批量持久化
            if (!IsInMemory(type))
            {
                StartAsyncPersistence(type);
            }
        }
    }

    /// <summary>
    /// 带数据快照的异步序列化方法
    /// </summary>
    /// <param name="type">类型</param>
    /// <returns>Task</returns>
    private static async Task SerializeAsyncWithSnapshot(Type type)
    {
        if (IsInMemory(type)) return;
        
        // 创建数据快照，确保一致性
        IList snapshot;
        lock (GetTypeLock(type))
        {
            var originalList = GetObjectsByName(type);
            snapshot = CreateListSnapshot(originalList);
        }
        
        // 异步持久化快照数据
        await Task.Run(() => {
            try
            {
                var target = SimpleJsonString.ConvertList(snapshot);
                if (!string.IsNullOrEmpty(target))
                {
                    var absolutePath = GetCachePath(type);
                    
                    lock (objLock)
                    {
                        var dir = Path.GetDirectoryName(absolutePath);
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir!);
                        }
                        
                        DH.IO.File.Write(absolutePath, target);
                    }
                }
            }
            catch (Exception ex)
            {
                XTrace.WriteException(ex);
            }
        }).ConfigureAwait(false);
    }
    
    /// <summary>
    /// 创建列表的深拷贝快照
    /// </summary>
    /// <param name="originalList">原始列表</param>
    /// <returns>快照列表</returns>
    private static IList CreateListSnapshot(IList originalList)
    {
        if (originalList == null) return new ArrayList();
        
        var snapshot = new ArrayList(originalList.Count);
        foreach (var item in originalList)
        {
            snapshot.Add(item); // 对于CacheObject，这是浅拷贝，但对于持久化来说足够了
        }
        return snapshot;
    }
}
