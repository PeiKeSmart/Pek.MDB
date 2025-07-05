using System.Diagnostics;

namespace DH.Data.Cache;

/// <summary>
/// 索引性能测试和对比类
/// 用于验证优化后的索引系统性能提升
/// </summary>
public class IndexPerformanceTest
{
    /// <summary>
    /// 测试索引性能对比
    /// </summary>
    public static void TestIndexPerformance()
    {
        Console.WriteLine("=== 索引性能优化测试 ===");
        Console.WriteLine();

        // 创建测试数据
        var testObjects = CreateTestObjects(1000);
        
        // 测试插入性能
        TestInsertPerformance(testObjects);
        
        // 测试查询性能
        TestQueryPerformance();
        
        // 测试更新性能
        TestUpdatePerformance(testObjects);
        
        // 测试删除性能
        TestDeletePerformance(testObjects);
    }

    private static List<TestCacheObject> CreateTestObjects(int count)
    {
        var objects = new List<TestCacheObject>();
        var random = new Random();
        
        for (int i = 0; i < count; i++)
        {
            objects.Add(new TestCacheObject
            {
                Name = $"TestObject_{i}",
                Category = $"Category_{random.Next(10)}",
                Value = random.Next(1000),
                Description = $"Description for object {i}"
            });
        }
        
        return objects;
    }

    private static void TestInsertPerformance(List<TestCacheObject> testObjects)
    {
        Console.WriteLine("1. 插入性能测试");
        
        var stopwatch = Stopwatch.StartNew();
        
        foreach (var obj in testObjects)
        {
            cdb.Insert(obj);
        }
        
        stopwatch.Stop();
        
        Console.WriteLine($"   插入 {testObjects.Count} 个对象耗时: {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"   平均每个对象: {(double)stopwatch.ElapsedMilliseconds / testObjects.Count:F2} ms");
        Console.WriteLine();
    }

    private static void TestQueryPerformance()
    {
        Console.WriteLine("2. 查询性能测试");
        
        var stopwatch = Stopwatch.StartNew();
        
        // 测试 ID 查询
        for (int i = 1; i <= 100; i++)
        {
            var obj = cdb.FindById<TestCacheObject>(i);
        }
        
        stopwatch.Stop();
        Console.WriteLine($"   ID查询100次耗时: {stopwatch.ElapsedMilliseconds} ms");
        
        // 测试属性查询
        stopwatch.Restart();
        
        for (int i = 0; i < 10; i++)
        {
            var results = cdb.FindBy<TestCacheObject>("Category", $"Category_{i}");
        }
        
        stopwatch.Stop();
        Console.WriteLine($"   属性查询10次耗时: {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine();
    }

    private static void TestUpdatePerformance(List<TestCacheObject> testObjects)
    {
        Console.WriteLine("3. 更新性能测试");
        
        var stopwatch = Stopwatch.StartNew();
        
        // 更新前100个对象
        for (int i = 0; i < Math.Min(100, testObjects.Count); i++)
        {
            var obj = cdb.FindById<TestCacheObject>(i + 1);
            if (obj != null)
            {
                obj.Description = $"Updated description {DateTime.Now.Ticks}";
                cdb.Update(obj);
            }
        }
        
        stopwatch.Stop();
        Console.WriteLine($"   更新100个对象耗时: {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine();
    }

    private static void TestDeletePerformance(List<TestCacheObject> testObjects)
    {
        Console.WriteLine("4. 删除性能测试");
        
        var stopwatch = Stopwatch.StartNew();
        
        // 删除最后50个对象
        int deleteCount = Math.Min(50, testObjects.Count);
        for (int i = testObjects.Count - deleteCount; i < testObjects.Count; i++)
        {
            var obj = cdb.FindById<TestCacheObject>(i + 1);
            if (obj != null)
            {
                cdb.Delete(obj);
            }
        }
        
        stopwatch.Stop();
        Console.WriteLine($"   删除{deleteCount}个对象耗时: {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine();
    }

    public static void ShowIndexStatistics()
    {
        Console.WriteLine("=== 索引统计信息 ===");
        
        var indexMap = MemoryDB.GetIndexMap();
        Console.WriteLine($"索引项总数: {indexMap.Count}");
        
        var objectsMap = MemoryDB.GetObjectsMap();
        Console.WriteLine($"对象类型数: {objectsMap.Count}");
        
        Console.WriteLine();
    }
}

/// <summary>
/// 测试用的缓存对象
/// </summary>
public class TestCacheObject : CacheObject
{
    public string Category { get; set; } = "";
    public int Value { get; set; }
    public string Description { get; set; } = "";
}
