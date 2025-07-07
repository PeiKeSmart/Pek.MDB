using DH.ORM;

namespace Pek.MDB.Examples;

/// <summary>
/// 用户实体 - 演示单字段唯一约束
/// </summary>
[CompositeUnique("FirstName", "LastName", "BirthDate")] // 复合唯一约束：姓名+生日组合唯一
public class User : CacheObject
{
    /// <summary>
    /// 用户名 - 必须全局唯一
    /// </summary>
    [Unique("用户名已存在，请选择其他用户名", AllowNull = false)]
    public string Username { get; set; } = "";

    /// <summary>
    /// 邮箱 - 必须全局唯一
    /// </summary>
    [Unique("邮箱地址已被注册", AllowNull = false)]
    public string Email { get; set; } = "";

    /// <summary>
    /// 手机号 - 可以为空，但不能重复
    /// </summary>
    [Unique("手机号已被使用")]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// 姓 - 参与复合唯一约束
    /// </summary>
    public string FirstName { get; set; } = "";

    /// <summary>
    /// 名 - 参与复合唯一约束
    /// </summary>
    public string LastName { get; set; } = "";

    /// <summary>
    /// 生日 - 参与复合唯一约束
    /// </summary>
    public DateTime BirthDate { get; set; }

    /// <summary>
    /// 普通字段 - 无唯一约束
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// 状态 - 普通字段
    /// </summary>
    public string Status { get; set; } = "Active";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 员工实体 - 演示多个复合唯一约束
/// </summary>
[CompositeUnique("CompanyUnique", "CompanyId", "EmployeeNumber")] // 公司内员工号唯一
[CompositeUnique("PersonalInfo", "IdCardNumber")] // 身份证号全局唯一（单字段复合约束示例）
public class Employee : CacheObject
{
    /// <summary>
    /// 公司ID
    /// </summary>
    public long CompanyId { get; set; }

    /// <summary>
    /// 员工编号（在同一公司内唯一）
    /// </summary>
    public string EmployeeNumber { get; set; } = "";

    /// <summary>
    /// 身份证号 - 全局唯一
    /// </summary>
    [Unique("身份证号已存在")]
    public string IdCardNumber { get; set; } = "";

    /// <summary>
    /// 工号 - 全局唯一
    /// </summary>
    [Unique("工号已被使用")]
    public string WorkNumber { get; set; } = "";

    /// <summary>
    /// 姓名
    /// </summary>
    public string FullName { get; set; } = "";

    /// <summary>
    /// 部门ID
    /// </summary>
    public long DepartmentId { get; set; }

    /// <summary>
    /// 职位
    /// </summary>
    public string Position { get; set; } = "";
}

/// <summary>
/// 产品实体 - 演示复杂的唯一约束场景
/// </summary>
[CompositeUnique("ProductCode", "CategoryId", "ProductCode")] // 分类内产品编码唯一
[CompositeUnique("SupplierProduct", "SupplierId", "SupplierProductCode")] // 供应商产品编码唯一
public class Product : CacheObject
{
    /// <summary>
    /// 产品SKU - 全局唯一
    /// </summary>
    [Unique("SKU已存在")]
    public string SKU { get; set; } = "";

    /// <summary>
    /// 产品编码（在同一分类内唯一）
    /// </summary>
    public string ProductCode { get; set; } = "";

    /// <summary>
    /// 分类ID
    /// </summary>
    public long CategoryId { get; set; }

    /// <summary>
    /// 供应商ID
    /// </summary>
    public long SupplierId { get; set; }

    /// <summary>
    /// 供应商产品编码（供应商内唯一）
    /// </summary>
    public string SupplierProductCode { get; set; } = "";

    /// <summary>
    /// 产品名称
    /// </summary>
    public string ProductName { get; set; } = "";

    /// <summary>
    /// 价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 条形码 - 可选的全局唯一标识
    /// </summary>
    [Unique("条形码已存在")]
    public string? Barcode { get; set; }
}