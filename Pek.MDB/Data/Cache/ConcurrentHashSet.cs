using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DH.Data.Cache
{
    /// <summary>
    /// 线程安全的HashSet实现
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ConcurrentHashSet<T> : IDisposable where T : notnull
    {
        private readonly ConcurrentDictionary<T, byte> _dictionary = new();
        private readonly ReaderWriterLockSlim _lock = new();
        private bool _disposed = false;

        public int Count => _dictionary.Count;

        public bool Add(T item)
        {
            _lock.EnterWriteLock();
            try
            {
                return _dictionary.TryAdd(item, 0);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Remove(T item)
        {
            _lock.EnterWriteLock();
            try
            {
                return _dictionary.TryRemove(item, out _);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Contains(T item)
        {
            _lock.EnterReadLock();
            try
            {
                return _dictionary.ContainsKey(item);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public HashSet<T> ToHashSet()
        {
            _lock.EnterReadLock();
            try
            {
                return new HashSet<T>(_dictionary.Keys);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _dictionary.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            _lock.EnterReadLock();
            try
            {
                return _dictionary.Keys.ToList().GetEnumerator();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _lock?.Dispose();
                _disposed = true;
            }
        }
    }
}
