using System;
using System.Collections.Generic;
using DH;

namespace Pek.MDB.Examples
{
    /// <summary>
    /// MemoryDB 优化功能使用示例
    /// </summary>
    public class OptimizedMemoryDBExample
    {
        /// <summary>
        /// 示例：批量插入操作
        /// </summary>
        public static void BatchInsertExample()
        {
            Console.WriteLine("=== 批量插入示例 ===");
            
            // 创建测试数据
            var users = new List<CacheObject>();
            for (int i = 1; i <= 1000; i++)
            {
                users.Add(new User 
                { 
                    Name = $"User{i}",
                    Email = $"user{i}@example.com",
                    Age = 20 + (i % 50),
                    Status = i % 3 == 0 ? "VIP" : "Normal"
                });
            }
            
            // 记录开始时间
            var startTime = DateTime.Now;
            
            // 批量插入
            MemoryDB.InsertBatch(users);
            
            var endTime = DateTime.Now;
            Console.WriteLine($"批量插入 {users.Count} 条记录耗时: {(endTime - startTime).TotalMilliseconds} ms");
        }
        
        /// <summary>
        /// 示例：分页查询操作
        /// </summary>
        public static void PagedQueryExample()
        {
            Console.WriteLine("\n=== 分页查询示例 ===");
            
            var pageSize = 10;
            var pageIndex = 0;
            
            // 分页查询 VIP 用户
            var vipUsers = MemoryDB.FindByPaged(typeof(User), "Status", "VIP", pageIndex, pageSize);
            
            Console.WriteLine($"第 {pageIndex + 1} 页 VIP 用户 ({pageSize} 条/页):");
            foreach (User user in vipUsers)
            {
                Console.WriteLine($"  - {user.Name} ({user.Email})");
            }
            
            // 查询下一页
            pageIndex++;
            var nextPageUsers = MemoryDB.FindByPaged(typeof(User), "Status", "VIP", pageIndex, pageSize);
            Console.WriteLine($"\n第 {pageIndex + 1} 页 VIP 用户:");
            foreach (User user in nextPageUsers)
            {
                Console.WriteLine($"  - {user.Name} ({user.Email})");
            }
        }
        
        /// <summary>
        /// 示例：性能监控功能
        /// </summary>
        public static void PerformanceMonitoringExample()
        {
            Console.WriteLine("\n=== 性能监控示例 ===");
            
            // 重置统计
            MemoryDB.ResetPerformanceStatistics();
            
            // 执行一些操作
            for (int i = 0; i < 100; i++)
            {
                MemoryDB.FindById(typeof(User), i % 50 + 1);
                if (i % 10 == 0)
                {
                    MemoryDB.FindBy(typeof(User), "Status", "VIP");
                }
            }
            
            // 获取性能统计
            var stats = MemoryDB.GetPerformanceStatistics();
            Console.WriteLine("操作统计:");
            foreach (var stat in stats)
            {
                Console.WriteLine($"  {stat.Key}: {stat.Value} 次");
            }
        }
        
        /// <summary>
        /// 示例：索引统计信息
        /// </summary>
        public static void IndexStatisticsExample()
        {
            Console.WriteLine("\n=== 索引统计示例 ===");
            
            // 获取索引统计
            var indexStats = MemoryDB.GetIndexStatistics();
            Console.WriteLine("索引统计信息:");
            foreach (var stat in indexStats)
            {
                Console.WriteLine($"  {stat.Key}: {stat.Value} 个对象");
            }
            
            // 获取总体索引信息
            var totalStats = MemoryDB.GetIndexStats();
            Console.WriteLine($"\n总体统计:");
            Console.WriteLine($"  索引总数: {totalStats.TotalIndexes}");
            Console.WriteLine($"  索引条目总数: {totalStats.TotalEntries}");
            Console.WriteLine($"  内存使用: {totalStats.MemoryUsage / 1024.0:F2} KB");
            
            // 清理空索引
            MemoryDB.CleanupEmptyIndexes();
            Console.WriteLine("已清理空索引");
        }
        
        /// <summary>
        /// 示例：并发操作测试
        /// </summary>
        public static void ConcurrentOperationsExample()
        {
            Console.WriteLine("\n=== 并发操作示例 ===");
            
            var tasks = new List<Task>();
            var insertCount = 0;
            var queryCount = 0;
            
            // 启动多个并发插入任务
            for (int t = 0; t < 4; t++)
            {
                int taskId = t;
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        var user = new User
                        {
                            Name = $"ConcurrentUser{taskId}_{i}",
                            Email = $"concurrent{taskId}_{i}@example.com",
                            Age = 25 + i % 40,
                            Status = "Normal"
                        };
                        MemoryDB.Insert(user);
                        Interlocked.Increment(ref insertCount);
                    }
                }));
            }
            
            // 启动多个并发查询任务
            for (int t = 0; t < 2; t++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        MemoryDB.FindBy(typeof(User), "Status", "Normal");
                        Interlocked.Increment(ref queryCount);
                    }
                }));
            }
            
            // 等待所有任务完成
            Task.WaitAll(tasks.ToArray());
            
            Console.WriteLine($"并发操作完成:");
            Console.WriteLine($"  插入操作: {insertCount} 次");
            Console.WriteLine($"  查询操作: {queryCount} 次");
        }
        
        /// <summary>
        /// 主函数 - 运行所有示例
        /// </summary>
        public static void Main()
        {
            try
            {
                Console.WriteLine("Pek.MDB 优化功能演示");
                Console.WriteLine("=====================");
                
                // 运行各个示例
                BatchInsertExample();
                PagedQueryExample();
                PerformanceMonitoringExample();
                IndexStatisticsExample();
                ConcurrentOperationsExample();
                
                Console.WriteLine("\n所有示例运行完成!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"示例运行出错: {ex.Message}");
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }
            
            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
    }
    
    /// <summary>
    /// 示例用户类
    /// </summary>
    public class User : CacheObject
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public int Age { get; set; }
        public string Status { get; set; } = "";
    }
}
