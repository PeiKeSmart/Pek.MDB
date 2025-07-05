using System;
using System.Collections.Generic;
using System.Linq;
using DH;
using DH.Data.Cache;
using DH.Data.Cache.TypedIndex;

namespace DH.Examples
{
    /// <summary>
    /// 测试类型感知索引功能的独立程序
    /// </summary>
    public class TypedIndexTestProgram
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("=== 类型感知索引功能测试 ===");
            
            try
            {
                // 测试基本功能
                TestBasicFunctionality();
                
                // 测试性能
                TestPerformance();
                
                // 测试索引管理
                TestIndexManagement();
                
                Console.WriteLine("\n=== 所有测试完成 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"测试过程中发生错误: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }
            
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }

        private static void TestBasicFunctionality()
        {
            Console.WriteLine("\n--- 测试基本功能 ---");
            
            // 启用类型感知索引
            cdb.EnableTypedIndex(true);
            
            // 添加测试数据
            var testData = new List<TestProduct>
            {
                new TestProduct { Id = 1, Name = "笔记本电脑", Price = 5999.99m, Category = "电子产品", CreateDate = DateTime.Now.AddDays(-10) },
                new TestProduct { Id = 2, Name = "智能手机", Price = 3299.00m, Category = "电子产品", CreateDate = DateTime.Now.AddDays(-5) },
                new TestProduct { Id = 3, Name = "办公椅", Price = 899.50m, Category = "办公用品", CreateDate = DateTime.Now.AddDays(-3) },
                new TestProduct { Id = 4, Name = "机械键盘", Price = 299.99m, Category = "电子产品", CreateDate = DateTime.Now.AddDays(-1) },
                new TestProduct { Id = 5, Name = "显示器", Price = 1999.00m, Category = "电子产品", CreateDate = DateTime.Now }
            };
            
            foreach (var product in testData)
            {
                cdb.Insert(product);
            }
            
            Console.WriteLine($"插入了 {testData.Count} 个测试产品");
            
            // 测试范围查询
            Console.WriteLine("\n测试价格范围查询 (500-2000):");
            var priceRangeResults = cdb.FindByRange<TestProduct>("Price", 500m, 2000m);
            foreach (var product in priceRangeResults)
            {
                Console.WriteLine($"  {product.Name}: ¥{product.Price}");
            }
            
            // 测试模糊查询
            Console.WriteLine("\n测试产品名称模糊查询 (包含'电'):");
            var nameSearchResults = cdb.FindByLike<TestProduct>("Name", "*电*");
            foreach (var product in nameSearchResults)
            {
                Console.WriteLine($"  {product.Name}: {product.Category}");
            }
            
            // 测试日期范围查询
            Console.WriteLine("\n测试创建日期范围查询 (最近7天):");
            var dateRangeResults = cdb.FindByRange<TestProduct>("CreateDate", DateTime.Now.AddDays(-7), DateTime.Now);
            foreach (var product in dateRangeResults)
            {
                Console.WriteLine($"  {product.Name}: {product.CreateDate:yyyy-MM-dd}");
            }
            
            // 测试多条件查询
            Console.WriteLine("\n测试多条件查询 (电子产品 AND 价格 > 1000):");
            var multiResults = cdb.FindByMultiple<TestProduct>(new Dictionary<string, object>
            {
                ["Category"] = "电子产品",
                ["Price"] = new { Min = 1000m }
            });
            foreach (var product in multiResults)
            {
                Console.WriteLine($"  {product.Name}: ¥{product.Price}");
            }
        }

        private static void TestPerformance()
        {
            Console.WriteLine("\n--- 测试性能 ---");
            
            // 启用类型感知索引
            cdb.EnableTypedIndex(true);
            
            // 生成大量测试数据
            var random = new Random();
            var categories = new[] { "电子产品", "办公用品", "家居用品", "服装", "食品" };
            
            Console.WriteLine("正在生成10000条测试数据...");
            var startTime = DateTime.Now;
            
            for (var i = 1; i <= 10000; i++)
            {
                var product = new TestProduct
                {
                    Id = i,
                    Name = $"产品{i}",
                    Price = (decimal)(random.NextDouble() * 10000),
                    Category = categories[random.Next(categories.Length)],
                    CreateDate = DateTime.Now.AddDays(-random.Next(365))
                };
                cdb.Insert(product);
            }
            
            var insertTime = DateTime.Now - startTime;
            Console.WriteLine($"插入10000条数据用时: {insertTime.TotalMilliseconds:F2} ms");
            
            // 测试查询性能
            Console.WriteLine("\n测试查询性能:");
            
            // 范围查询性能
            startTime = DateTime.Now;
            var rangeResults = cdb.FindByRange<TestProduct>("Price", 1000m, 5000m);
            var rangeTime = DateTime.Now - startTime;
            Console.WriteLine($"价格范围查询 (1000-5000): 找到 {rangeResults.Count()} 条记录，用时 {rangeTime.TotalMilliseconds:F2} ms");
            
            // 精确查询性能
            startTime = DateTime.Now;
            var exactResults = cdb.FindBy<TestProduct>("Category", "电子产品");
            var exactTime = DateTime.Now - startTime;
            Console.WriteLine($"类别精确查询 (电子产品): 找到 {exactResults.Count()} 条记录，用时 {exactTime.TotalMilliseconds:F2} ms");
            
            // 日期范围查询性能
            startTime = DateTime.Now;
            var dateResults = cdb.FindByRange<TestProduct>("CreateDate", DateTime.Now.AddDays(-30), DateTime.Now);
            var dateTime = DateTime.Now - startTime;
            Console.WriteLine($"日期范围查询 (最近30天): 找到 {dateResults.Count()} 条记录，用时 {dateTime.TotalMilliseconds:F2} ms");
        }

        private static void TestIndexManagement()
        {
            Console.WriteLine("\n--- 测试索引管理 ---");
            
            // 启用类型感知索引
            cdb.EnableTypedIndex(true);
            
            // 添加一些测试数据
            for (var i = 1; i <= 100; i++)
            {
                var product = new TestProduct
                {
                    Id = i,
                    Name = $"产品{i}",
                    Price = i * 10m,
                    Category = i % 2 == 0 ? "偶数产品" : "奇数产品",
                    CreateDate = DateTime.Now.AddDays(-i)
                };
                cdb.Insert(product);
            }
            
            Console.WriteLine("索引管理功能测试:");
            Console.WriteLine($"  类型感知索引状态: {(cdb.IsTypedIndexEnabled() ? "已启用" : "已禁用")}");
            Console.WriteLine("  插入了100条测试数据用于索引管理测试");
            
            // 测试各种查询性能
            var startTime = DateTime.Now;
            var results1 = cdb.FindByRange<TestProduct>("Price", 100m, 500m);
            var time1 = DateTime.Now - startTime;
            Console.WriteLine($"  价格范围查询 (100-500): 找到 {results1.Count} 条记录，用时 {time1.TotalMilliseconds:F2} ms");
            
            startTime = DateTime.Now;
            var results2 = cdb.FindBy<TestProduct>("Category", "偶数产品");
            var time2 = DateTime.Now - startTime;
            Console.WriteLine($"  类别精确查询 (偶数产品): 找到 {results2.Count} 条记录，用时 {time2.TotalMilliseconds:F2} ms");
            
            startTime = DateTime.Now;
            var results3 = cdb.FindByDateRange<TestProduct>("CreateDate", DateTime.Now.AddDays(-50), DateTime.Now.AddDays(-10));
            var time3 = DateTime.Now - startTime;
            Console.WriteLine($"  日期范围查询 (最近10-50天): 找到 {results3.Count} 条记录，用时 {time3.TotalMilliseconds:F2} ms");
            
            Console.WriteLine("\n索引管理功能验证完成");
        }
    }

    /// <summary>
    /// 测试用的产品类
    /// </summary>
    public class TestProduct : CacheObject
    {
        public decimal Price { get; set; }
        public string Category { get; set; } = string.Empty;
        public DateTime CreateDate { get; set; }
    }
}
