namespace DH.ORM;

/// <summary>
/// 明确标记属性需要建立索引
/// 主要用于文档化重要的查询字段，提高代码可读性
/// </summary>
[Serializable, AttributeUsage(AttributeTargets.Property)]
public class IndexedAttribute : Attribute
{
    /// <summary>
    /// 索引优先级，数值越大优先级越高
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 索引说明
    /// </summary>
    public string Description { get; set; } = "";

    public IndexedAttribute()
    {
    }

    public IndexedAttribute(string description)
    {
        Description = description;
    }

    public IndexedAttribute(int priority, string description = "")
    {
        Priority = priority;
        Description = description;
    }
}
