using DH;
using DH.ORM;

namespace Pek.MDB.Examples;

/// <summary>
/// 演示新的索引标记功能的示例
/// </summary>
public class Product : CacheObject
{
    /// <summary>
    /// 产品名称 - 默认建立索引（向后兼容）
    /// </summary>
    public new string Name { get; set; } = "";

    /// <summary>
    /// 产品价格 - 默认建立索引（向后兼容）
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 分类ID - 明确要求索引（主要用于文档化重要查询字段）
    /// </summary>
    [Indexed("重要查询字段")]
    public string CategoryId { get; set; } = "";

    /// <summary>
    /// 产品描述 - 需要保存但不需要索引（大文本字段）
    /// </summary>
    [NotIndexed("大文本字段，不适合建立索引")]
    public string Description { get; set; } = "";

    /// <summary>
    /// 产品详情 - 需要保存但不需要索引（长文本）
    /// </summary>
    [NotIndexed("长文本内容")]
    public string Details { get; set; } = "";

    /// <summary>
    /// 含税价格 - 完全不保存（临时计算属性）
    /// </summary>
    [NotSave]
    public decimal PriceWithTax { get; set; }

    /// <summary>
    /// 最后计算时间 - 完全不保存（缓存时间戳）
    /// </summary>
    [NotSave]
    public DateTime LastCalculated { get; set; }

    /// <summary>
    /// 是否在线 - 完全不保存（运行时状态）
    /// </summary>
    [NotSave]
    public bool IsOnline { get; set; }

    /// <summary>
    /// 供应商ID - 高优先级索引
    /// </summary>
    [Indexed(priority: 10, description: "供应商查询")]
    public string SupplierId { get; set; } = "";

    /// <summary>
    /// 库存数量 - 普通索引
    /// </summary>
    [Indexed]
    public int Stock { get; set; }
}

/// <summary>
/// 演示索引标记功能使用的测试类
/// </summary>
public class IndexAttributeDemo
{
    public static void RunDemo()
    {
        Console.WriteLine("=== 索引标记功能演示 ===");
        
        // 创建测试产品
        var product = new Product
        {
            Name = "Test Product",
            Price = 100.00m,
            CategoryId = "CAT001",
            Description = "这是一个很长的产品描述，包含大量文本内容...",
            Details = "详细信息：产品规格、使用方法、注意事项等...",
            SupplierId = "SUP001",
            Stock = 50
        };

        // 计算临时属性（不会被索引和保存）
        product.PriceWithTax = product.Price * 1.13m;
        product.LastCalculated = DateTime.Now;
        product.IsOnline = true;

        Console.WriteLine($"产品名称: {product.Name}");
        Console.WriteLine($"价格: {product.Price:C}");
        Console.WriteLine($"含税价格: {product.PriceWithTax:C} (不会被保存)");
        Console.WriteLine($"描述: {product.Description.Substring(0, 20)}... (会保存但不索引)");
        Console.WriteLine($"库存: {product.Stock} (会被索引)");
        
        // 插入数据库
        MemoryDB.Insert(product);
        
        Console.WriteLine("\n=== 索引行为说明 ===");
        Console.WriteLine("✓ Name: 默认建立索引（向后兼容）");
        Console.WriteLine("✓ Price: 默认建立索引（向后兼容）");
        Console.WriteLine("✓ CategoryId: [Indexed] 明确要求索引");
        Console.WriteLine("✗ Description: [NotIndexed] 不建立索引");
        Console.WriteLine("✗ Details: [NotIndexed] 不建立索引");
        Console.WriteLine("✗ PriceWithTax: [NotSave] 不保存不索引");
        Console.WriteLine("✗ LastCalculated: [NotSave] 不保存不索引");
        Console.WriteLine("✗ IsOnline: [NotSave] 不保存不索引");
        Console.WriteLine("✓ SupplierId: [Indexed] 高优先级索引");
        Console.WriteLine("✓ Stock: [Indexed] 普通索引");
        
        Console.WriteLine("\n=== 兼容性验证 ===");
        Console.WriteLine("- 现有项目无需修改代码");
        Console.WriteLine("- 默认所有属性都建立索引");
        Console.WriteLine("- NotSaveAttribute 优先级最高");
        Console.WriteLine("- 可以渐进式使用新标记进行优化");
    }
}

/// <summary>
/// 演示增量索引更新的示例
/// </summary>
public class IncrementalIndexDemo
{
    public static void RunDemo()
    {
        Console.WriteLine("\n=== 增量索引更新演示 ===");
        
        var product = new Product
        {
            Name = "Original Name",
            Price = 100.00m,
            CategoryId = "CAT001",
            Stock = 10
        };

        // 插入产品
        MemoryDB.Insert(product);
        Console.WriteLine($"插入产品: {product.Name}, 价格: {product.Price:C}");
        
        // 更新部分属性
        product.Name = "Updated Name";
        product.Price = 120.00m;
        // CategoryId 和 Stock 保持不变
        
        Console.WriteLine($"更新产品: {product.Name}, 价格: {product.Price:C}");
        
        // 使用增量更新 - 只更新变化的属性索引
        MemoryDB.Update(product);
        
        Console.WriteLine("\n增量更新优化:");
        Console.WriteLine("✓ 只更新 Name 和 Price 的索引");
        Console.WriteLine("✓ CategoryId 和 Stock 的索引保持不变");
        Console.WriteLine("✓ 大幅提升更新性能");
    }
}
