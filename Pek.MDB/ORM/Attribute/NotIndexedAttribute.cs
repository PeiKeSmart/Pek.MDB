namespace DH.ORM;

/// <summary>
/// 标记属性不需要建立索引
/// 用于优化大文本字段或不常用于查询的字段
/// </summary>
[Serializable, AttributeUsage(AttributeTargets.Property)]
public class NotIndexedAttribute : Attribute
{
    /// <summary>
    /// 不建立索引的原因说明
    /// </summary>
    public string Reason { get; set; } = "";

    public NotIndexedAttribute()
    {
    }

    public NotIndexedAttribute(string reason)
    {
        Reason = reason;
    }
}
