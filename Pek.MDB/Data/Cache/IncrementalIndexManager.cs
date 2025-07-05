using DH.ORM;
using DH.Reflection;
using System.Collections.Concurrent;
using System.Reflection;

namespace DH.Data.Cache;

/// <summary>
/// 增量索引更新管理器
/// 负责跟踪对象属性变化，只更新变化的属性索引
/// </summary>
internal class IncrementalIndexManager
{
    // 缓存对象的属性值快照，用于比较变化
    private static readonly ConcurrentDictionary<long, Dictionary<string, object>> _objectSnapshots = new();
    
    // 缓存类型的索引属性信息
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _indexableProperties = new();

    /// <summary>
    /// 获取类型的可索引属性列表
    /// </summary>
    public static PropertyInfo[] GetIndexableProperties(Type type)
    {
        return _indexableProperties.GetOrAdd(type, t =>
        {
            var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var indexableProps = new List<PropertyInfo>();

            foreach (var prop in properties)
            {
                if (ShouldCreateIndex(prop))
                {
                    indexableProps.Add(prop);
                }
            }

            return indexableProps.ToArray();
        });
    }

    /// <summary>
    /// 判断属性是否应该建立索引
    /// 优先级：NotSaveAttribute > NotIndexedAttribute > IndexedAttribute > 默认索引
    /// </summary>
    public static bool ShouldCreateIndex(PropertyInfo property)
    {
        // 第一优先级：NotSaveAttribute（完全不保存，当然也不索引）
        if (rft.GetAttribute(property, typeof(NotSaveAttribute)) != null)
            return false;

        // 第二优先级：明确的索引控制
        if (rft.GetAttribute(property, typeof(NotIndexedAttribute)) != null)
            return false;

        if (rft.GetAttribute(property, typeof(IndexedAttribute)) != null)
            return true;

        // 第三优先级：默认行为（保持现有兼容性）
        return true;  // 默认建立索引，保持向后兼容
    }

    /// <summary>
    /// 创建对象的属性值快照
    /// </summary>
    public static void CreateSnapshot(CacheObject obj)
    {
        if (obj == null) return;

        var properties = GetIndexableProperties(obj.GetType());
        var snapshot = new Dictionary<string, object>();

        foreach (var prop in properties)
        {
            if (prop.CanRead)
            {
                var value = rft.GetPropertyValue(obj, prop.Name);
                snapshot[prop.Name] = value;
            }
        }

        _objectSnapshots[obj.Id] = snapshot;
    }

    /// <summary>
    /// 获取对象属性的变化列表
    /// </summary>
    public static List<PropertyChangeInfo> GetChangedProperties(CacheObject obj)
    {
        if (obj == null) return new List<PropertyChangeInfo>();

        var changes = new List<PropertyChangeInfo>();
        var properties = GetIndexableProperties(obj.GetType());

        // 获取旧快照
        _objectSnapshots.TryGetValue(obj.Id, out var oldSnapshot);
        if (oldSnapshot == null)
        {
            // 没有旧快照，表示所有属性都是新的
            foreach (var prop in properties)
            {
                if (prop.CanRead)
                {
                    var newValue = rft.GetPropertyValue(obj, prop.Name);
                    changes.Add(new PropertyChangeInfo
                    {
                        PropertyName = prop.Name,
                        OldValue = null,
                        NewValue = newValue,
                        IsNew = true
                    });
                }
            }
        }
        else
        {
            // 比较属性变化
            foreach (var prop in properties)
            {
                if (prop.CanRead)
                {
                    var newValue = rft.GetPropertyValue(obj, prop.Name);
                    oldSnapshot.TryGetValue(prop.Name, out var oldValue);

                    if (!Equals(oldValue, newValue))
                    {
                        changes.Add(new PropertyChangeInfo
                        {
                            PropertyName = prop.Name,
                            OldValue = oldValue,
                            NewValue = newValue,
                            IsNew = false
                        });
                    }
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// 更新对象快照
    /// </summary>
    public static void UpdateSnapshot(CacheObject obj)
    {
        CreateSnapshot(obj);
    }

    /// <summary>
    /// 删除对象快照
    /// </summary>
    public static void RemoveSnapshot(long objectId)
    {
        _objectSnapshots.TryRemove(objectId, out _);
    }

    /// <summary>
    /// 清空所有快照
    /// </summary>
    public static void ClearSnapshots()
    {
        _objectSnapshots.Clear();
        _indexableProperties.Clear();
    }
}

/// <summary>
/// 属性变化信息
/// </summary>
public class PropertyChangeInfo
{
    public string PropertyName { get; set; } = "";
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public bool IsNew { get; set; }

    public override string ToString()
    {
        if (IsNew)
            return $"{PropertyName}: (new) -> {NewValue}";
        else
            return $"{PropertyName}: {OldValue} -> {NewValue}";
    }
}
