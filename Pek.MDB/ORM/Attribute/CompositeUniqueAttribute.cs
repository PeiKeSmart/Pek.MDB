namespace DH.ORM;

/// <summary>
/// 标记类需要复合字段唯一性约束
/// 用于确保多个字段的组合值在整个表中唯一
/// </summary>
[Serializable, AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class CompositeUniqueAttribute : Attribute
{
    /// <summary>
    /// 参与唯一性约束的字段名称数组
    /// </summary>
    public string[] Fields { get; }

    /// <summary>
    /// 唯一性验证失败时的错误消息
    /// </summary>
    public string ErrorMessage { get; set; }

    /// <summary>
    /// 约束组名（用于区分同一类上的多个复合约束）
    /// </summary>
    public string GroupName { get; set; }

    /// <summary>
    /// 是否允许组合中包含null值（默认允许）
    /// </summary>
    public bool AllowNull { get; set; } = true;

    public CompositeUniqueAttribute(params string[] fields)
    {
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        if (fields.Length < 2)
            throw new ArgumentException("复合唯一约束至少需要2个字段", nameof(fields));
        
        ErrorMessage = $"字段组合 [{string.Join(", ", fields)}] 必须唯一";
        GroupName = string.Join("_", fields);
    }

    public CompositeUniqueAttribute(string groupName, params string[] fields)
    {
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        if (fields.Length < 2)
            throw new ArgumentException("复合唯一约束至少需要2个字段", nameof(fields));
        
        GroupName = groupName;
        ErrorMessage = $"字段组合 [{string.Join(", ", fields)}] 必须唯一";
    }
}