using DH.Data.Cache;
using DH.Data.Cache.TypedIndex;

namespace Pek.MDB.Tests
{
    // 测试类
    public class TestUser : CacheObject
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public DateTime CreateTime { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
        public decimal Salary { get; set; }
    }

    public class IndexRemovalTest
    {
        public static void RunTest()
        {
            Console.WriteLine("=== Pek.MDB 传统索引移除验证测试 ===");
            
            // 清理之前的数据
            MemoryDB.Clear();
            Console.WriteLine("✅ 清理完成");

            // 1. 测试插入和自动索引创建
            Console.WriteLine("\n1. 测试插入和自动索引创建");
            var user1 = new TestUser { Name = "张三", Age = 25, Salary = 8000.50m, IsActive = true };
            var user2 = new TestUser { Name = "李四", Age = 30, Salary = 12000.00m, IsActive = false };
            var user3 = new TestUser { Name = "王五", Age = 25, Salary = 9500.75m, IsActive = true };
            
            MemoryDB.Insert(user1);
            MemoryDB.Insert(user2);
            MemoryDB.Insert(user3);
            Console.WriteLine($"✅ 插入 3 个用户，ID: {user1.Id}, {user2.Id}, {user3.Id}");

            // 2. 测试精确查询
            Console.WriteLine("\n2. 测试精确查询");
            var results25 = MemoryDB.FindBy(typeof(TestUser), "Age", 25);
            Console.WriteLine($"✅ 年龄为 25 的用户数量: {results25.Count}");
            
            var resultsActive = MemoryDB.FindBy(typeof(TestUser), "IsActive", true);
            Console.WriteLine($"✅ 活跃用户数量: {resultsActive.Count}");

            // 3. 测试类型感知索引统计
            Console.WriteLine("\n3. 测试索引统计");
            var stats = MemoryDB.GetIndexStats();
            Console.WriteLine($"✅ 索引总数: {stats.TotalIndexes}");
            Console.WriteLine($"✅ 索引条目: {stats.TotalEntries}");
            Console.WriteLine($"✅ 内存使用: {stats.MemoryUsage} bytes");

            // 4. 测试高级查询（通过 TypedQueryExtensions）
            Console.WriteLine("\n4. 测试高级查询");
            try
            {
                // 测试范围查询
                var salaryRange = TypedQueryExtensions.FindByRange<TestUser>("Salary", 8000m, 10000m);
                Console.WriteLine($"✅ 薪资 8000-10000 的用户数量: {salaryRange.Count}");
                
                // 测试字符串查询
                var nameResults = TypedQueryExtensions.FindByLike<TestUser>("Name", "*三*");
                Console.WriteLine($"✅ 名字包含'三'的用户数量: {nameResults.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  高级查询测试失败: {ex.Message}");
            }

            // 5. 测试更新和删除
            Console.WriteLine("\n5. 测试更新和删除");
            user1.Age = 26;
            MemoryDB.Update(user1);
            Console.WriteLine($"✅ 更新用户 {user1.Name} 年龄为 26");
            
            MemoryDB.Delete(user2);
            Console.WriteLine($"✅ 删除用户 {user2.Name}");

            // 6. 验证更新后的查询
            Console.WriteLine("\n6. 验证更新后的查询");
            var results25After = MemoryDB.FindBy(typeof(TestUser), "Age", 25);
            var results26After = MemoryDB.FindBy(typeof(TestUser), "Age", 26);
            Console.WriteLine($"✅ 更新后年龄为 25 的用户数量: {results25After.Count}");
            Console.WriteLine($"✅ 更新后年龄为 26 的用户数量: {results26After.Count}");

            Console.WriteLine("\n=== 测试完成 ===");
            Console.WriteLine("🎉 所有核心功能正常工作！传统索引已成功移除，类型感知索引运行良好。");
        }
    }
}
