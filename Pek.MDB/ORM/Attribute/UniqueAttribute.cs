namespace DH.ORM;

/// <summary>
/// 标记属性需要唯一性约束
/// 用于确保字段值在整个表中唯一
/// </summary>
[Serializable, AttributeUsage(AttributeTargets.Property)]
public class UniqueAttribute : Attribute
{
    /// <summary>
    /// 唯一性验证失败时的错误消息
    /// </summary>
    public string ErrorMessage { get; set; } = "值必须唯一";

    /// <summary>
    /// 是否允许null值（默认允许）
    /// </summary>
    public bool AllowNull { get; set; } = true;

    /// <summary>
    /// 约束组名（用于标识同组约束）
    /// </summary>
    public string GroupName { get; set; } = "";

    public UniqueAttribute()
    {
    }

    public UniqueAttribute(string errorMessage)
    {
        ErrorMessage = errorMessage;
    }

    public UniqueAttribute(string errorMessage, bool allowNull)
    {
        ErrorMessage = errorMessage;
        AllowNull = allowNull;
    }
}