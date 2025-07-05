using System.Collections.Concurrent;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 日期时间类型索引 - 支持时间范围查询
/// </summary>
public class DateTimeIndex : TypedIndexBase
{
    // 使用有序字典来支持范围查询
    private readonly SortedDictionary<DateTime, HashSet<long>> _sortedIndex = new();
    private readonly ConcurrentDictionary<string, HashSet<long>> _yearIndex = new();
    private readonly ConcurrentDictionary<string, HashSet<long>> _monthIndex = new();
    private readonly ConcurrentDictionary<string, HashSet<long>> _dayIndex = new();
    private readonly object _sortedLock = new();

    public override void AddId(object value, long id)
    {
        if (value == null) return;

        var dateTimeValue = ConvertToDateTime(value);
        if (dateTimeValue == null) return;

        var dt = dateTimeValue.Value;

        // 添加到基础索引
        base.AddId(dt, id);

        // 添加到有序索引
        lock (_sortedLock)
        {
            if (!_sortedIndex.TryGetValue(dt, out var idSet))
            {
                idSet = new HashSet<long>();
                _sortedIndex[dt] = idSet;
            }
            idSet.Add(id);
        }

        // 添加到时间分组索引
        var year = dt.Year.ToString();
        var month = $"{dt.Year}-{dt.Month:00}";
        var day = $"{dt.Year}-{dt.Month:00}-{dt.Day:00}";

        _yearIndex.AddOrUpdate(year, new HashSet<long> { id }, (key, set) => { set.Add(id); return set; });
        _monthIndex.AddOrUpdate(month, new HashSet<long> { id }, (key, set) => { set.Add(id); return set; });
        _dayIndex.AddOrUpdate(day, new HashSet<long> { id }, (key, set) => { set.Add(id); return set; });
    }

    public override void RemoveId(object value, long id)
    {
        if (value == null) return;

        var dateTimeValue = ConvertToDateTime(value);
        if (dateTimeValue == null) return;

        var dt = dateTimeValue.Value;

        // 从基础索引移除
        base.RemoveId(dt, id);

        // 从有序索引移除
        lock (_sortedLock)
        {
            if (_sortedIndex.TryGetValue(dt, out var idSet))
            {
                idSet.Remove(id);
                if (idSet.Count == 0)
                {
                    _sortedIndex.Remove(dt);
                }
            }
        }

        // 从时间分组索引移除
        var year = dt.Year.ToString();
        var month = $"{dt.Year}-{dt.Month:00}";
        var day = $"{dt.Year}-{dt.Month:00}-{dt.Day:00}";

        RemoveFromTimeIndex(_yearIndex, year, id);
        RemoveFromTimeIndex(_monthIndex, month, id);
        RemoveFromTimeIndex(_dayIndex, day, id);
    }

    public override HashSet<long> GetRange(IComparable min, IComparable max)
    {
        var minValue = ConvertToDateTime(min);
        var maxValue = ConvertToDateTime(max);

        if (minValue == null || maxValue == null)
        {
            return new HashSet<long>();
        }

        Interlocked.Increment(ref _queryCount);
        _lastAccessed = DateTime.Now;

        var result = new HashSet<long>();
        lock (_sortedLock)
        {
            var range = _sortedIndex.Where(kvp => kvp.Key >= minValue && kvp.Key <= maxValue);
            foreach (var kvp in range)
            {
                foreach (var id in kvp.Value)
                {
                    result.Add(id);
                }
            }
        }

        if (result.Count > 0)
        {
            Interlocked.Increment(ref _hitCount);
        }

        return result;
    }

    public override HashSet<long> GetByPattern(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return new HashSet<long>();
        }

        Interlocked.Increment(ref _queryCount);
        _lastAccessed = DateTime.Now;

        var result = new HashSet<long>();

        // 支持年份、年月、年月日查询
        if (pattern.Length == 4 && int.TryParse(pattern, out _))
        {
            // 年份查询：2024
            if (_yearIndex.TryGetValue(pattern, out var yearSet))
            {
                foreach (var id in yearSet)
                {
                    result.Add(id);
                }
            }
        }
        else if (pattern.Length == 7 && pattern.Contains("-"))
        {
            // 年月查询：2024-01
            if (_monthIndex.TryGetValue(pattern, out var monthSet))
            {
                foreach (var id in monthSet)
                {
                    result.Add(id);
                }
            }
        }
        else if (pattern.Length == 10 && pattern.Count(c => c == '-') == 2)
        {
            // 年月日查询：2024-01-15
            if (_dayIndex.TryGetValue(pattern, out var daySet))
            {
                foreach (var id in daySet)
                {
                    result.Add(id);
                }
            }
        }

        if (result.Count > 0)
        {
            Interlocked.Increment(ref _hitCount);
        }

        return result;
    }

    /// <summary>
    /// 按年份查询
    /// </summary>
    public HashSet<long> GetByYear(int year)
    {
        return GetByPattern(year.ToString());
    }

    /// <summary>
    /// 按年月查询
    /// </summary>
    public HashSet<long> GetByYearMonth(int year, int month)
    {
        return GetByPattern($"{year}-{month:00}");
    }

    /// <summary>
    /// 按年月日查询
    /// </summary>
    public HashSet<long> GetByDate(int year, int month, int day)
    {
        return GetByPattern($"{year}-{month:00}-{day:00}");
    }

    public override void Clear()
    {
        base.Clear();
        lock (_sortedLock)
        {
            _sortedIndex.Clear();
        }
        _yearIndex.Clear();
        _monthIndex.Clear();
        _dayIndex.Clear();
    }

    private void RemoveFromTimeIndex(ConcurrentDictionary<string, HashSet<long>> index, string key, long id)
    {
        if (index.TryGetValue(key, out var idSet))
        {
            idSet.Remove(id);
            if (idSet.Count == 0)
            {
                index.TryRemove(key, out _);
            }
        }
    }

    private DateTime? ConvertToDateTime(object value)
    {
        if (value == null) return null;

        try
        {
            return value switch
            {
                DateTime dt => dt,
                DateTimeOffset dto => dto.DateTime,
                string str when DateTime.TryParse(str, out var dt) => dt,
                long ticks => new DateTime(ticks),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    protected override long EstimateMemoryUsage()
    {
        var baseSize = base.EstimateMemoryUsage();
        var sortedSize = _sortedIndex.Count * (sizeof(long) + sizeof(long) * 2); // DateTime的Ticks + 额外开销
        var yearSize = _yearIndex.Sum(kvp => kvp.Key.Length * sizeof(char) + kvp.Value.Count * sizeof(long));
        var monthSize = _monthIndex.Sum(kvp => kvp.Key.Length * sizeof(char) + kvp.Value.Count * sizeof(long));
        var daySize = _dayIndex.Sum(kvp => kvp.Key.Length * sizeof(char) + kvp.Value.Count * sizeof(long));
        
        return baseSize + sortedSize + yearSize + monthSize + daySize;
    }
}
