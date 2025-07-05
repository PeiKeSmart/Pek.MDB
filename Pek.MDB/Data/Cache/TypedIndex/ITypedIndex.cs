using System.Collections.Concurrent;

namespace DH.Data.Cache.TypedIndex;

/// <summary>
/// 类型感知索引接口
/// </summary>
public interface ITypedIndex
{
    /// <summary>
    /// 添加索引项
    /// </summary>
    /// <param name="value">索引值</param>
    /// <param name="id">对象ID</param>
    void AddId(object value, long id);

    /// <summary>
    /// 移除索引项
    /// </summary>
    /// <param name="value">索引值</param>
    /// <param name="id">对象ID</param>
    void RemoveId(object value, long id);

    /// <summary>
    /// 根据精确值获取ID集合
    /// </summary>
    /// <param name="value">索引值</param>
    /// <returns>ID集合</returns>
    HashSet<long> GetIds(object value);

    /// <summary>
    /// 根据范围获取ID集合
    /// </summary>
    /// <param name="min">最小值</param>
    /// <param name="max">最大值</param>
    /// <returns>ID集合</returns>
    HashSet<long> GetRange(IComparable min, IComparable max);

    /// <summary>
    /// 根据模式匹配获取ID集合
    /// </summary>
    /// <param name="pattern">匹配模式</param>
    /// <returns>ID集合</returns>
    HashSet<long> GetByPattern(string pattern);

    /// <summary>
    /// 清空索引
    /// </summary>
    void Clear();

    /// <summary>
    /// 获取索引统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    IndexStatistics GetStatistics();
}

/// <summary>
/// 索引统计信息
/// </summary>
public class IndexStatistics
{
    /// <summary>
    /// 属性名称
    /// </summary>
    public string PropertyName { get; set; } = "";

    /// <summary>
    /// 索引类型
    /// </summary>
    public string IndexType { get; set; } = "";

    /// <summary>
    /// 查询次数
    /// </summary>
    public int QueryCount { get; set; }

    /// <summary>
    /// 命中次数
    /// </summary>
    public int HitCount { get; set; }

    /// <summary>
    /// 命中率
    /// </summary>
    public double HitRate => QueryCount > 0 ? (double)HitCount / QueryCount : 0;

    /// <summary>
    /// 索引大小（键值对数量）
    /// </summary>
    public int IndexSize { get; set; }

    /// <summary>
    /// 索引的唯一值数量
    /// </summary>
    public int UniqueValueCount { get; set; }

    /// <summary>
    /// 最后访问时间
    /// </summary>
    public DateTime LastAccessed { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedTime { get; set; }

    /// <summary>
    /// 内存使用估计（字节）
    /// </summary>
    public long MemoryUsage { get; set; }
}
