using System.Collections;

using DH.Data.Cache;
using DH.Data.Cache.TypedIndex;

namespace DH;

/// <summary>
/// 从内存数据库中查询数据
/// </summary>
/// <remarks>
/// 数据持久化在 /data/ 目录下，以json格式存储。加载之后常驻内存。
/// 特点：直接从内存中检索，速度相当于 Hashtable。插入和更新较慢(相对而言)，因为插入和更新会在内存中重建索引。
/// </remarks>
public class cdb
{
    /// <summary>
    /// 查询类型 T 的所有数据
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns>返回所有数据的列表</returns>
    public static List<T> FindAll<T>() where T : CacheObject
    {
        IList list = MemoryDB.FindAll(typeof(T));
        return db.getResults<T>(list);
    }

    /// <summary>
    /// 根据 id 查询某条数据
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns>返回某条数据</returns>
    public static T FindById<T>(long id) where T : CacheObject
    {
        Object obj = MemoryDB.FindById(typeof(T), id);
        return (T)obj;
    }

    /// <summary>
    /// 根据 id 获取对象的名称
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    public static String FindNameById<T>(int id) where T : CacheObject
    {
        T obj = cdb.FindById<T>(id);
        if (obj == null) return "";
        return obj.Name;
    }

    /// <summary>
    /// 根据属性查询数据。框架已经给对象的所有属性做了索引。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="propertyName">属性名称</param>
    /// <param name="val">属性的值</param>
    /// <returns>返回数据列表</returns>
    public static List<T> FindBy<T>(String propertyName, Object val) where T : CacheObject
    {
        var list = MemoryDB.FindBy(typeof(T), propertyName, val);
        return db.getResults<T>(list);
    }

    /// <summary>
    /// 插入数据，并对所有属性做索引，速度较慢。新插入的数据会被同步持久化到磁盘。
    /// </summary>
    public static void Insert(CacheObject obj)
    {
        MemoryDB.Insert(obj);
    }

    /// <summary>
    /// 插入时，只针对特定属性做索引，提高速度
    /// </summary>
    /// <param name="propertyName">需要做索引的属性</param>
    /// <param name="pValue">属性的值</param>
    /// <param name="obj"></param>
    public static void InsertByIndex(CacheObject obj, String propertyName, Object pValue)
    {
        MemoryDB.InsertByIndex(obj, propertyName, pValue);
    }

    /// <summary>
    /// 更新对象，并将对象同步持久化的磁盘，同时更新索引
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public static Result Update(CacheObject obj)
    {
        return MemoryDB.Update(obj);
    }

    /// <summary>
    /// 从内存中删除数据，并同步磁盘中内容。
    /// </summary>
    /// <param name="obj"></param>
    public static void Delete(CacheObject obj)
    {
        MemoryDB.Delete(obj);
    }

    /// <summary>
    /// 根据范围查询数据（类型感知）
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="propertyName">属性名称</param>
    /// <param name="minValue">最小值</param>
    /// <param name="maxValue">最大值</param>
    /// <returns>返回数据列表</returns>
    public static List<T> FindByRange<T>(String propertyName, IComparable minValue, IComparable maxValue) where T : CacheObject
    {
        var type = typeof(T);
        var idSet = TypedIndexManager.FindByRange(type, propertyName, minValue, maxValue);
        
        var results = new List<T>();
        foreach (var id in idSet)
        {
            var obj = MemoryDB.FindById(type, id);
            if (obj is T typedObj)
            {
                results.Add(typedObj);
            }
        }
        
        return results;
    }

    /// <summary>
    /// 根据模式匹配查询数据（支持通配符）
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="propertyName">属性名称</param>
    /// <param name="pattern">匹配模式（支持*通配符）</param>
    /// <returns>返回数据列表</returns>
    public static List<T> FindByLike<T>(String propertyName, String pattern) where T : CacheObject
    {
        var type = typeof(T);
        var idSet = TypedIndexManager.FindByPattern(type, propertyName, pattern);
        
        var results = new List<T>();
        foreach (var id in idSet)
        {
            var obj = MemoryDB.FindById(type, id);
            if (obj is T typedObj)
            {
                results.Add(typedObj);
            }
        }
        
        return results;
    }

    /// <summary>
    /// 复合条件查询
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="conditions">查询条件字典</param>
    /// <returns>返回数据列表</returns>
    public static List<T> FindByMultiple<T>(Dictionary<String, Object> conditions) where T : CacheObject
    {
        if (conditions == null || conditions.Count == 0)
        {
            return new List<T>();
        }

        var type = typeof(T);
        HashSet<long>? resultSet = null;

        foreach (var condition in conditions)
        {
            var propertyName = condition.Key;
            var value = condition.Value;
            
            var currentSet = TypedIndexManager.FindByValue(type, propertyName, value);
            
            if (resultSet == null)
            {
                resultSet = currentSet;
            }
            else
            {
                // 求交集
                resultSet.IntersectWith(currentSet);
            }
            
            // 如果交集为空，提前退出
            if (resultSet.Count == 0)
            {
                break;
            }
        }

        if (resultSet == null || resultSet.Count == 0)
        {
            return new List<T>();
        }

        var results = new List<T>();
        foreach (var id in resultSet)
        {
            var obj = MemoryDB.FindById(type, id);
            if (obj is T typedObj)
            {
                results.Add(typedObj);
            }
        }
        
        return results;
    }

    /// <summary>
    /// 批量ID查询
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="ids">ID集合</param>
    /// <returns>ID到对象的映射</returns>
    public static Dictionary<long, T> FindByIds<T>(IEnumerable<long> ids) where T : CacheObject
    {
        var type = typeof(T);
        var results = new Dictionary<long, T>();
        
        foreach (var id in ids)
        {
            var obj = MemoryDB.FindById(type, id);
            if (obj is T typedObj)
            {
                results[id] = typedObj;
            }
        }
        
        return results;
    }

    /// <summary>
    /// 分页查询 - 现代化分页API，推荐使用
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="propertyName">排序属性名称（可选）</param>
    /// <param name="pageIndex">页索引（从0开始）</param>
    /// <param name="pageSize">页大小</param>
    /// <param name="ascending">是否升序</param>
    /// <returns>分页结果，包含数据和分页信息</returns>
    /// <example>
    /// // 基本分页：获取第一页的20条用户数据
    /// var users = cdb.FindByPage&lt;User&gt;(pageIndex: 0, pageSize: 20);
    /// 
    /// // 带排序的分页：按创建时间倒序分页
    /// var recentUsers = cdb.FindByPage&lt;User&gt;("CreateTime", 0, 50, false);
    /// 
    /// // 客户端LINQ分页（适合复杂查询）：
    /// var activeUsers = cdb.FindBy&lt;User&gt;("Status", "Active")
    ///     .Where(u => u.CreateTime > DateTime.Today.AddDays(-30))
    ///     .Skip(pageIndex * pageSize)
    ///     .Take(pageSize)
    ///     .ToList();
    /// </example>
    public static PagedResult<T> FindByPage<T>(String? propertyName = null, int pageIndex = 0, int pageSize = 10, bool ascending = true) where T : CacheObject
    {
        var type = typeof(T);
        var allObjects = MemoryDB.FindAll(type);
        
        var typedObjects = allObjects.Cast<T>();
        
        // 如果指定了排序属性，进行排序
        if (!string.IsNullOrEmpty(propertyName))
        {
            var property = type.GetProperty(propertyName);
            if (property != null)
            {
                typedObjects = ascending 
                    ? typedObjects.OrderBy(obj => property.GetValue(obj))
                    : typedObjects.OrderByDescending(obj => property.GetValue(obj));
            }
        }

        var totalCount = typedObjects.Count();
        var pagedItems = typedObjects.Skip(pageIndex * pageSize).Take(pageSize).ToList();
        
        return new PagedResult<T>
        {
            Items = pagedItems,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    /// <summary>
    /// 根据唯一字段值查找对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="propertyName">唯一字段名称</param>
    /// <param name="value">要查找的值</param>
    /// <returns>找到的对象，如果不存在返回null</returns>
    public static T? FindByUnique<T>(String propertyName, Object value) where T : CacheObject
    {
        var type = typeof(T);
        var objectId = UniqueConstraintManager.FindByUniqueValue(type, propertyName, value);
        
        if (objectId.HasValue)
        {
            return MemoryDB.FindById(type, objectId.Value) as T;
        }
        
        return null;
    }

    /// <summary>
    /// 根据复合唯一约束查找对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="groupName">约束组名</param>
    /// <param name="fieldValues">字段值字典</param>
    /// <returns>找到的对象，如果不存在返回null</returns>
    public static T? FindByCompositeUnique<T>(String groupName, Dictionary<String, Object> fieldValues) where T : CacheObject
    {
        if (fieldValues == null || fieldValues.Count == 0)
            return null;
        
        var type = typeof(T);
        
        // 构建复合键
        var keyParts = new List<string>();
        foreach (var kvp in fieldValues.OrderBy(x => x.Key)) // 按键名排序确保一致性
        {
            keyParts.Add(kvp.Value?.ToString() ?? "NULL");
        }
        var compositeKey = string.Join("|", keyParts);
        
        var objectId = UniqueConstraintManager.FindByCompositeUniqueValue(type, groupName, compositeKey);
        
        if (objectId.HasValue)
        {
            return MemoryDB.FindById(type, objectId.Value) as T;
        }
        
        return null;
    }

    /// <summary>
    /// 检查唯一字段值是否已存在
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="propertyName">唯一字段名称</param>
    /// <param name="value">要检查的值</param>
    /// <param name="excludeId">排除的对象ID（用于更新时检查）</param>
    /// <returns>如果值已存在返回true，否则返回false</returns>
    public static bool IsUniqueValueExists<T>(String propertyName, Object value, long? excludeId = null) where T : CacheObject
    {
        var type = typeof(T);
        var existingId = UniqueConstraintManager.FindByUniqueValue(type, propertyName, value);
        
        if (!existingId.HasValue)
            return false;
        
        return !excludeId.HasValue || existingId.Value != excludeId.Value;
    }

    /// <summary>
    /// 批量插入对象（支持唯一约束验证）
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="objects">要插入的对象集合</param>
    public static void InsertBatch<T>(IEnumerable<T> objects) where T : CacheObject
    {
        if (objects == null) return;
        
        MemoryDB.InsertBatch(objects.Cast<CacheObject>());
    }

    /// <summary>
    /// 获取唯一约束统计信息
    /// </summary>
    /// <returns>统计信息元组</returns>
    public static (int SingleFieldConstraints, int CompositeFieldConstraints) GetUniqueConstraintStatistics()
    {
        return UniqueConstraintManager.GetStatistics();
    }
}

/// <summary>
/// 分页结果
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// 当前页的数据项
    /// </summary>
    public List<T> Items { get; set; } = new();

    /// <summary>
    /// 页索引（从0开始）
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// 页大小
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// 总记录数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 总页数
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// 是否有上一页
    /// </summary>
    public bool HasPreviousPage => PageIndex > 0;

    /// <summary>
    /// 是否有下一页
    /// </summary>
    public bool HasNextPage => PageIndex < TotalPages - 1;
}
