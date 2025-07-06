using System.Collections;
using System.Collections.Concurrent;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 线程安全的HashSet实现
/// </summary>
/// <typeparam name="T">元素类型</typeparam>
public class ConcurrentHashSet<T> : ISet<T>
{
    private readonly ConcurrentDictionary<T, byte> _dictionary = new();

    public bool Add(T item)
    {
        return _dictionary.TryAdd(item, 0);
    }

    public bool Remove(T item)
    {
        return _dictionary.TryRemove(item, out _);
    }

    public bool Contains(T item)
    {
        return _dictionary.ContainsKey(item);
    }

    public int Count => _dictionary.Count;

    public bool IsReadOnly => false;

    public void Clear()
    {
        _dictionary.Clear();
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _dictionary.Keys.CopyTo(array, arrayIndex);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _dictionary.Keys.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        foreach (var item in other)
        {
            Remove(item);
        }
    }

    public void IntersectWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        var otherSet = new HashSet<T>(other);
        var keysToRemove = new List<T>();
        
        foreach (var key in _dictionary.Keys)
        {
            if (!otherSet.Contains(key))
            {
                keysToRemove.Add(key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            Remove(key);
        }
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        var otherSet = new HashSet<T>(other);
        return Count < otherSet.Count && IsSubsetOf(otherSet);
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        var otherSet = new HashSet<T>(other);
        return Count > otherSet.Count && IsSupersetOf(otherSet);
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        var otherSet = new HashSet<T>(other);
        return _dictionary.Keys.All(otherSet.Contains);
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        return other.All(Contains);
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        return other.Any(Contains);
    }

    public bool SetEquals(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        var otherSet = new HashSet<T>(other);
        return Count == otherSet.Count && IsSupersetOf(otherSet);
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        foreach (var item in other)
        {
            if (Contains(item))
            {
                Remove(item);
            }
            else
            {
                Add(item);
            }
        }
    }

    public void UnionWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        
        foreach (var item in other)
        {
            Add(item);
        }
    }

    void ICollection<T>.Add(T item)
    {
        Add(item);
    }

    /// <summary>
    /// 创建当前集合的快照副本
    /// </summary>
    /// <returns>包含当前所有元素的新HashSet</returns>
    public HashSet<T> ToHashSet()
    {
        return new HashSet<T>(_dictionary.Keys);
    }
}
