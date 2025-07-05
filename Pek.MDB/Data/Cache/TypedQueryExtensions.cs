using System.Collections;
using DH.Data.Cache.TypedIndex;

namespace DH;

/// <summary>
/// 扩展的类型感知查询方法
/// </summary>
public static class TypedQueryExtensions
{
    /// <summary>
    /// 根据范围查询数据
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="propertyName">属性名称</param>
    /// <param name="minValue">最小值</param>
    /// <param name="maxValue">最大值</param>
    /// <returns>匹配的数据列表</returns>
    public static List<T> FindByRange<T>(string propertyName, IComparable minValue, IComparable maxValue) where T : CacheObject
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
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="propertyName">属性名称</param>
    /// <param name="pattern">匹配模式（支持*通配符）</param>
    /// <returns>匹配的数据列表</returns>
    public static List<T> FindByLike<T>(string propertyName, string pattern) where T : CacheObject
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
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="conditions">查询条件字典</param>
    /// <returns>匹配的数据列表</returns>
    public static List<T> FindByMultiple<T>(Dictionary<string, object> conditions) where T : CacheObject
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
    /// <typeparam name="T">数据类型</typeparam>
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
    /// 分页查询
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="propertyName">排序属性名称</param>
    /// <param name="pageIndex">页索引（从0开始）</param>
    /// <param name="pageSize">页大小</param>
    /// <param name="ascending">是否升序</param>
    /// <returns>分页结果</returns>
    public static PagedResult<T> FindByPage<T>(string? propertyName = null, int pageIndex = 0, int pageSize = 10, bool ascending = true) where T : CacheObject
    {
        var type = typeof(T);
        var allObjects = MemoryDB.FindAll(type);
        
        IEnumerable<T> typedObjects = allObjects.Cast<T>();
        
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
    /// 数值范围查询的便捷方法
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="propertyName">数值属性名称</param>
    /// <param name="min">最小值</param>
    /// <param name="max">最大值</param>
    /// <returns>匹配的数据列表</returns>
    public static List<T> FindByNumericRange<T>(string propertyName, decimal min, decimal max) where T : CacheObject
    {
        return FindByRange<T>(propertyName, min, max);
    }

    /// <summary>
    /// 日期范围查询的便捷方法
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="propertyName">日期属性名称</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>匹配的数据列表</returns>
    public static List<T> FindByDateRange<T>(string propertyName, DateTime startDate, DateTime endDate) where T : CacheObject
    {
        return FindByRange<T>(propertyName, startDate, endDate);
    }

    /// <summary>
    /// 字符串包含查询的便捷方法
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="propertyName">字符串属性名称</param>
    /// <param name="searchText">搜索文本</param>
    /// <returns>匹配的数据列表</returns>
    public static List<T> FindByContains<T>(string propertyName, string searchText) where T : CacheObject
    {
        return FindByLike<T>(propertyName, $"*{searchText}*");
    }

    /// <summary>
    /// 字符串开头匹配查询的便捷方法
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="propertyName">字符串属性名称</param>
    /// <param name="prefix">前缀</param>
    /// <returns>匹配的数据列表</returns>
    public static List<T> FindByStartsWith<T>(string propertyName, string prefix) where T : CacheObject
    {
        return FindByLike<T>(propertyName, $"{prefix}*");
    }

    /// <summary>
    /// 字符串结尾匹配查询的便捷方法
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="propertyName">字符串属性名称</param>
    /// <param name="suffix">后缀</param>
    /// <returns>匹配的数据列表</returns>
    public static List<T> FindByEndsWith<T>(string propertyName, string suffix) where T : CacheObject
    {
        return FindByLike<T>(propertyName, $"*{suffix}");
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
