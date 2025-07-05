using DH.Data.Cache.TypedIndex;

namespace DH.Data.Cache;

/// <summary>
/// 索引管理器 - 提供基本索引统计功能
/// </summary>
public static class IndexManager
{
    /// <summary>
    /// 获取基本索引统计信息
    /// </summary>
    /// <param name="type">对象类型</param>
    /// <param name="propertyName">属性名称</param>
    /// <returns>基本统计信息</returns>
    public static BasicIndexStats? GetBasicStats(Type type, string propertyName)
    {
        var index = TypedIndexManager.GetIndex(type, propertyName);
        if (index == null) return null;

        var stats = index.GetStatistics();
        return new BasicIndexStats
        {
            PropertyName = propertyName,
            QueryCount = stats.QueryCount,
            HitCount = stats.HitCount,
            HitRate = stats.QueryCount > 0 ? (double)stats.HitCount / stats.QueryCount : 0
        };
    }

    /// <summary>
    /// 获取索引总数
    /// </summary>
    /// <returns>索引总数</returns>
    public static int GetIndexCount()
    {
        return TypedIndexManager.GetIndexCount();
    }
}

/// <summary>
/// 基本索引统计信息
/// </summary>
public class BasicIndexStats
{
    /// <summary>
    /// 属性名称
    /// </summary>
    public string PropertyName { get; set; } = "";

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
    public double HitRate { get; set; }
}
