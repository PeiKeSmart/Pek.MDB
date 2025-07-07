namespace DH.ORM;

/// <summary>
/// 唯一性约束违反异常
/// 当插入或更新的数据违反唯一性约束时抛出
/// </summary>
[Serializable]
public class UniqueConstraintViolationException : Exception
{
    /// <summary>
    /// 违反约束的字段名称
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// 违反约束的值
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// 约束类型（Single 或 Composite）
    /// </summary>
    public string ConstraintType { get; }

    /// <summary>
    /// 冲突的对象ID
    /// </summary>
    public long ConflictingObjectId { get; }

    public UniqueConstraintViolationException(string message) : base(message)
    {
    }

    public UniqueConstraintViolationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public UniqueConstraintViolationException(string propertyName, object value, string message) 
        : base(message)
    {
        PropertyName = propertyName;
        Value = value;
        ConstraintType = "Single";
    }

    public UniqueConstraintViolationException(string propertyName, object value, long conflictingObjectId, string message) 
        : base(message)
    {
        PropertyName = propertyName;
        Value = value;
        ConflictingObjectId = conflictingObjectId;
        ConstraintType = "Single";
    }

    public UniqueConstraintViolationException(string[] propertyNames, object[] values, string message) 
        : base(message)
    {
        PropertyName = string.Join(", ", propertyNames);
        Value = string.Join("|", values?.Select(v => v?.ToString() ?? "NULL") ?? new[] { "NULL" });
        ConstraintType = "Composite";
    }

    public override string ToString()
    {
        var details = $"约束类型: {ConstraintType}, 字段: {PropertyName}, 值: {Value}";
        if (ConflictingObjectId > 0)
        {
            details += $", 冲突对象ID: {ConflictingObjectId}";
        }
        return $"{base.ToString()}\n详细信息: {details}";
    }
}