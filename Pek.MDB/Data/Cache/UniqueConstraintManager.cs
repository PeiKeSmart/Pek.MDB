using System.Collections.Concurrent;
using System.Reflection;
using DH.Reflection;
using DH.ORM;

namespace DH.Data.Cache;

/// <summary>
/// 唯一约束管理器
/// 负责管理和验证单字段和复合字段的唯一性约束
/// </summary>
public static class UniqueConstraintManager
{
    // 单字段唯一索引：Type_PropertyName -> Value -> ObjectId
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<object, long>> 
        _singleFieldUnique = new();
    
    // 复合字段唯一索引：Type_GroupName -> CompositeKey -> ObjectId
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> 
        _compositeFieldUnique = new();

    /// <summary>
    /// 验证对象的所有唯一约束
    /// </summary>
    /// <param name="obj">要验证的对象</param>
    /// <param name="excludeId">排除的对象ID（用于更新操作）</param>
    public static void ValidateAllUniqueConstraints(CacheObject obj, long? excludeId = null)
    {
        var type = obj.GetType();
        
        // 验证单字段唯一约束
        ValidateSingleFieldConstraints(type, obj, excludeId);
        
        // 验证复合字段唯一约束
        ValidateCompositeFieldConstraints(type, obj, excludeId);
    }

    /// <summary>
    /// 添加对象的唯一约束索引
    /// </summary>
    /// <param name="obj">要添加索引的对象</param>
    public static void AddUniqueConstraints(CacheObject obj)
    {
        var type = obj.GetType();
        
        // 添加单字段唯一索引
        AddSingleFieldConstraints(type, obj);
        
        // 添加复合字段唯一索引
        AddCompositeFieldConstraints(type, obj);
    }

    /// <summary>
    /// 移除对象的唯一约束索引
    /// </summary>
    /// <param name="obj">要移除索引的对象</param>
    public static void RemoveUniqueConstraints(CacheObject obj)
    {
        var type = obj.GetType();
        
        // 移除单字段唯一索引
        RemoveSingleFieldConstraints(type, obj);
        
        // 移除复合字段唯一索引
        RemoveCompositeFieldConstraints(type, obj);
    }

    /// <summary>
    /// 更新对象的唯一约束索引
    /// </summary>
    /// <param name="oldObj">旧对象</param>
    /// <param name="newObj">新对象</param>
    public static void UpdateUniqueConstraints(CacheObject oldObj, CacheObject newObj)
    {
        // 先验证新对象的唯一性（排除当前对象）
        ValidateAllUniqueConstraints(newObj, oldObj.Id);
        
        // 移除旧索引
        RemoveUniqueConstraints(oldObj);
        
        // 添加新索引
        AddUniqueConstraints(newObj);
    }

    #region 单字段唯一约束处理

    private static void ValidateSingleFieldConstraints(Type type, CacheObject obj, long? excludeId)
    {
        var properties = type.GetProperties();
        foreach (var prop in properties)
        {
            var uniqueAttr = prop.GetCustomAttribute<UniqueAttribute>();
            if (uniqueAttr != null)
            {
                var value = prop.GetValue(obj);
                
                // 检查null值处理
                if (value == null)
                {
                    if (!uniqueAttr.AllowNull)
                    {
                        throw new UniqueConstraintViolationException(
                            prop.Name, null, $"字段 {prop.Name} 不允许为空值");
                    }
                    continue; // null值不参与唯一性检查
                }
                
                if (!ValidateSingleUnique(type, prop.Name, value, excludeId))
                {
                    var conflictingId = GetConflictingObjectId(type, prop.Name, value);
                    throw new UniqueConstraintViolationException(
                        prop.Name, value, conflictingId, 
                        $"字段 {prop.Name} 的值 '{value}' 已存在。{uniqueAttr.ErrorMessage}");
                }
            }
        }
    }

    private static void AddSingleFieldConstraints(Type type, CacheObject obj)
    {
        var properties = type.GetProperties();
        foreach (var prop in properties)
        {
            var uniqueAttr = prop.GetCustomAttribute<UniqueAttribute>();
            if (uniqueAttr != null)
            {
                var value = prop.GetValue(obj);
                if (value != null) // 只为非null值建立索引
                {
                    AddSingleUniqueIndex(type, prop.Name, value, obj.Id);
                }
            }
        }
    }

    private static void RemoveSingleFieldConstraints(Type type, CacheObject obj)
    {
        var properties = type.GetProperties();
        foreach (var prop in properties)
        {
            var uniqueAttr = prop.GetCustomAttribute<UniqueAttribute>();
            if (uniqueAttr != null)
            {
                var value = prop.GetValue(obj);
                if (value != null)
                {
                    RemoveSingleUniqueIndex(type, prop.Name, value);
                }
            }
        }
    }

    private static bool ValidateSingleUnique(Type type, string propertyName, object value, long? excludeId)
    {
        var indexKey = GetSingleIndexKey(type, propertyName);
        var uniqueIndex = _singleFieldUnique.GetOrAdd(indexKey, 
            _ => new ConcurrentDictionary<object, long>());
        
        if (uniqueIndex.TryGetValue(value, out var existingId))
        {
            return excludeId.HasValue && existingId == excludeId.Value;
        }
        
        return true; // 值不存在，验证通过
    }

    private static void AddSingleUniqueIndex(Type type, string propertyName, object value, long objectId)
    {
        var indexKey = GetSingleIndexKey(type, propertyName);
        var uniqueIndex = _singleFieldUnique.GetOrAdd(indexKey, 
            _ => new ConcurrentDictionary<object, long>());
        
        if (!uniqueIndex.TryAdd(value, objectId))
        {
            throw new UniqueConstraintViolationException(
                propertyName, value, $"字段 {propertyName} 的值 '{value}' 已存在");
        }
    }

    private static void RemoveSingleUniqueIndex(Type type, string propertyName, object value)
    {
        var indexKey = GetSingleIndexKey(type, propertyName);
        if (_singleFieldUnique.TryGetValue(indexKey, out var uniqueIndex))
        {
            uniqueIndex.TryRemove(value, out _);
            
            // 如果索引为空，考虑移除整个索引（可选优化）
            if (uniqueIndex.IsEmpty)
            {
                _singleFieldUnique.TryRemove(indexKey, out _);
            }
        }
    }

    private static long GetConflictingObjectId(Type type, string propertyName, object value)
    {
        var indexKey = GetSingleIndexKey(type, propertyName);
        if (_singleFieldUnique.TryGetValue(indexKey, out var uniqueIndex))
        {
            uniqueIndex.TryGetValue(value, out var objectId);
            return objectId;
        }
        return 0;
    }

    private static string GetSingleIndexKey(Type type, string propertyName)
    {
        return $"{type.FullName}_{propertyName}";
    }

    #endregion

    #region 复合字段唯一约束处理

    private static void ValidateCompositeFieldConstraints(Type type, CacheObject obj, long? excludeId)
    {
        var compositeAttrs = type.GetCustomAttributes<CompositeUniqueAttribute>();
        foreach (var compositeAttr in compositeAttrs)
        {
            if (!ValidateCompositeUnique(type, compositeAttr, obj, excludeId))
            {
                var values = GetFieldValues(compositeAttr.Fields, obj);
                throw new UniqueConstraintViolationException(
                    compositeAttr.Fields, values, compositeAttr.ErrorMessage);
            }
        }
    }

    private static void AddCompositeFieldConstraints(Type type, CacheObject obj)
    {
        var compositeAttrs = type.GetCustomAttributes<CompositeUniqueAttribute>();
        foreach (var compositeAttr in compositeAttrs)
        {
            var compositeKey = BuildCompositeKey(compositeAttr.Fields, obj);
            if (!string.IsNullOrEmpty(compositeKey)) // 只为有效组合建立索引
            {
                AddCompositeUniqueIndex(type, compositeAttr.GroupName, compositeKey, obj.Id);
            }
        }
    }

    private static void RemoveCompositeFieldConstraints(Type type, CacheObject obj)
    {
        var compositeAttrs = type.GetCustomAttributes<CompositeUniqueAttribute>();
        foreach (var compositeAttr in compositeAttrs)
        {
            var compositeKey = BuildCompositeKey(compositeAttr.Fields, obj);
            if (!string.IsNullOrEmpty(compositeKey))
            {
                RemoveCompositeUniqueIndex(type, compositeAttr.GroupName, compositeKey);
            }
        }
    }

    private static bool ValidateCompositeUnique(Type type, CompositeUniqueAttribute constraint, 
        CacheObject obj, long? excludeId)
    {
        var compositeKey = BuildCompositeKey(constraint.Fields, obj);
        if (string.IsNullOrEmpty(compositeKey))
        {
            return constraint.AllowNull; // 如果包含null值，根据AllowNull设置决定
        }
        
        var indexKey = GetCompositeIndexKey(type, constraint.GroupName);
        var uniqueIndex = _compositeFieldUnique.GetOrAdd(indexKey, 
            _ => new ConcurrentDictionary<string, long>());
        
        if (uniqueIndex.TryGetValue(compositeKey, out var existingId))
        {
            return excludeId.HasValue && existingId == excludeId.Value;
        }
        
        return true;
    }

    private static void AddCompositeUniqueIndex(Type type, string groupName, string compositeKey, long objectId)
    {
        var indexKey = GetCompositeIndexKey(type, groupName);
        var uniqueIndex = _compositeFieldUnique.GetOrAdd(indexKey, 
            _ => new ConcurrentDictionary<string, long>());
        
        if (!uniqueIndex.TryAdd(compositeKey, objectId))
        {
            throw new UniqueConstraintViolationException(
                $"复合约束 {groupName} 的组合值已存在");
        }
    }

    private static void RemoveCompositeUniqueIndex(Type type, string groupName, string compositeKey)
    {
        var indexKey = GetCompositeIndexKey(type, groupName);
        if (_compositeFieldUnique.TryGetValue(indexKey, out var uniqueIndex))
        {
            uniqueIndex.TryRemove(compositeKey, out _);
            
            // 如果索引为空，考虑移除整个索引（可选优化）
            if (uniqueIndex.IsEmpty)
            {
                _compositeFieldUnique.TryRemove(indexKey, out _);
            }
        }
    }

    private static string BuildCompositeKey(string[] fields, CacheObject obj)
    {
        var keyParts = new List<string>();
        var type = obj.GetType();
        bool hasNull = false;
        
        foreach (var fieldName in fields)
        {
            var property = type.GetProperty(fieldName);
            if (property == null)
                throw new ArgumentException($"属性 {fieldName} 在类型 {type.Name} 中不存在");
            
            var value = property.GetValue(obj);
            if (value == null)
            {
                hasNull = true;
                keyParts.Add("NULL");
            }
            else
            {
                keyParts.Add(value.ToString());
            }
        }
        
        // 如果包含null值，返回空字符串（由调用方根据AllowNull决定处理）
        return hasNull ? string.Empty : string.Join("|", keyParts);
    }

    private static object[] GetFieldValues(string[] fields, CacheObject obj)
    {
        var values = new object[fields.Length];
        var type = obj.GetType();
        
        for (int i = 0; i < fields.Length; i++)
        {
            var property = type.GetProperty(fields[i]);
            values[i] = property?.GetValue(obj);
        }
        
        return values;
    }

    private static string GetCompositeIndexKey(Type type, string groupName)
    {
        return $"{type.FullName}_Composite_{groupName}";
    }

    #endregion

    #region 公共查询方法

    /// <summary>
    /// 根据唯一字段值查找对象ID
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="value">要查找的值</param>
    /// <returns>对象ID，如果不存在返回null</returns>
    public static long? FindByUniqueValue(Type type, string propertyName, object value)
    {
        var indexKey = GetSingleIndexKey(type, propertyName);
        if (_singleFieldUnique.TryGetValue(indexKey, out var uniqueIndex))
        {
            return uniqueIndex.TryGetValue(value, out var objectId) ? objectId : null;
        }
        return null;
    }

    /// <summary>
    /// 根据复合唯一字段组合查找对象ID
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="groupName">约束组名</param>
    /// <param name="compositeKey">复合键</param>
    /// <returns>对象ID，如果不存在返回null</returns>
    public static long? FindByCompositeUniqueValue(Type type, string groupName, string compositeKey)
    {
        var indexKey = GetCompositeIndexKey(type, groupName);
        if (_compositeFieldUnique.TryGetValue(indexKey, out var uniqueIndex))
        {
            return uniqueIndex.TryGetValue(compositeKey, out var objectId) ? objectId : null;
        }
        return null;
    }

    /// <summary>
    /// 清除指定类型的所有唯一约束索引
    /// </summary>
    /// <param name="type">要清除的类型</param>
    public static void ClearUniqueConstraints(Type type)
    {
        var typePrefix = type.FullName + "_";
        
        // 清除单字段唯一索引
        var singleKeysToRemove = _singleFieldUnique.Keys
            .Where(key => key.StartsWith(typePrefix))
            .ToList();
        
        foreach (var key in singleKeysToRemove)
        {
            _singleFieldUnique.TryRemove(key, out _);
        }
        
        // 清除复合字段唯一索引
        var compositeKeysToRemove = _compositeFieldUnique.Keys
            .Where(key => key.StartsWith(typePrefix))
            .ToList();
        
        foreach (var key in compositeKeysToRemove)
        {
            _compositeFieldUnique.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 获取唯一约束统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    public static (int SingleFieldConstraints, int CompositeFieldConstraints) GetStatistics()
    {
        return (_singleFieldUnique.Count, _compositeFieldUnique.Count);
    }

    #endregion
}