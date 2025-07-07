using DH;
using DH.ORM;

namespace Pek.MDB.Examples;

/// <summary>
/// 唯一约束功能使用示例
/// </summary>
public static class UniqueConstraintUsageExamples
{
    /// <summary>
    /// 基础唯一约束使用示例
    /// </summary>
    public static void BasicUniqueConstraintExample()
    {
        Console.WriteLine("=== 基础唯一约束示例 ===");

        try
        {
            // 1. 创建第一个用户 - 成功
            var user1 = new User
            {
                Username = "admin",
                Email = "admin@example.com",
                FirstName = "张",
                LastName = "三",
                BirthDate = new DateTime(1990, 1, 1),
                DisplayName = "管理员"
            };
            cdb.Insert(user1);
            Console.WriteLine($"✓ 用户1创建成功，ID: {user1.Id}");

            // 2. 尝试创建重复用户名的用户 - 失败
            var user2 = new User
            {
                Username = "admin", // 重复的用户名
                Email = "admin2@example.com",
                FirstName = "李",
                LastName = "四",
                BirthDate = new DateTime(1991, 2, 2)
            };
            cdb.Insert(user2);
            Console.WriteLine("❌ 这行不应该执行到");
        }
        catch (UniqueConstraintViolationException ex)
        {
            Console.WriteLine($"✓ 正确捕获唯一约束违反: {ex.Message}");
        }

        try
        {
            // 3. 创建不同用户名但重复邮箱的用户 - 失败
            var user3 = new User
            {
                Username = "user2",
                Email = "admin@example.com", // 重复的邮箱
                FirstName = "王",
                LastName = "五",
                BirthDate = new DateTime(1992, 3, 3)
            };
            cdb.Insert(user3);
            Console.WriteLine("❌ 这行不应该执行到");
        }
        catch (UniqueConstraintViolationException ex)
        {
            Console.WriteLine($"✓ 正确捕获邮箱重复: {ex.Message}");
        }

        // 4. 创建完全不同的用户 - 成功
        var user4 = new User
        {
            Username = "user2",
            Email = "user2@example.com",
            FirstName = "王",
            LastName = "五",
            BirthDate = new DateTime(1992, 3, 3),
            PhoneNumber = "13800138000"
        };
        cdb.Insert(user4);
        Console.WriteLine($"✓ 用户4创建成功，ID: {user4.Id}");
    }

    /// <summary>
    /// 复合唯一约束使用示例
    /// </summary>
    public static void CompositeUniqueConstraintExample()
    {
        Console.WriteLine("\n=== 复合唯一约束示例 ===");

        try
        {
            // 1. 创建第一个员工 - 成功
            var emp1 = new Employee
            {
                CompanyId = 1001,
                EmployeeNumber = "E001",
                IdCardNumber = "110101199001011234",
                WorkNumber = "W001",
                FullName = "张三",
                DepartmentId = 1,
                Position = "开发工程师"
            };
            cdb.Insert(emp1);
            Console.WriteLine($"✓ 员工1创建成功，ID: {emp1.Id}");

            // 2. 在同一公司创建相同员工号的员工 - 失败
            var emp2 = new Employee
            {
                CompanyId = 1001, // 同一公司
                EmployeeNumber = "E001", // 重复的员工号
                IdCardNumber = "110101199002022345",
                WorkNumber = "W002",
                FullName = "李四",
                DepartmentId = 2,
                Position = "产品经理"
            };
            cdb.Insert(emp2);
            Console.WriteLine("❌ 这行不应该执行到");
        }
        catch (UniqueConstraintViolationException ex)
        {
            Console.WriteLine($"✓ 正确捕获复合约束违反: {ex.Message}");
        }

        // 3. 在不同公司创建相同员工号的员工 - 成功
        var emp3 = new Employee
        {
            CompanyId = 1002, // 不同公司
            EmployeeNumber = "E001", // 相同员工号但不同公司，允许
            IdCardNumber = "110101199003033456",
            WorkNumber = "W003",
            FullName = "王五",
            DepartmentId = 1,
            Position = "UI设计师"
        };
        cdb.Insert(emp3);
        Console.WriteLine($"✓ 员工3创建成功（不同公司），ID: {emp3.Id}");

        try
        {
            // 4. 创建重复身份证号的员工 - 失败
            var emp4 = new Employee
            {
                CompanyId = 1003,
                EmployeeNumber = "E002",
                IdCardNumber = "110101199001011234", // 重复的身份证号
                WorkNumber = "W004",
                FullName = "赵六",
                DepartmentId = 1,
                Position = "测试工程师"
            };
            cdb.Insert(emp4);
            Console.WriteLine("❌ 这行不应该执行到");
        }
        catch (UniqueConstraintViolationException ex)
        {
            Console.WriteLine($"✓ 正确捕获身份证号重复: {ex.Message}");
        }
    }

    /// <summary>
    /// 唯一约束查询示例
    /// </summary>
    public static void UniqueConstraintQueryExample()
    {
        Console.WriteLine("\n=== 唯一约束查询示例 ===");

        // 1. 根据唯一字段查找用户
        var userByUsername = cdb.FindByUnique<User>("Username", "admin");
        if (userByUsername != null)
        {
            Console.WriteLine($"✓ 通过用户名找到用户: {userByUsername.DisplayName} (ID: {userByUsername.Id})");
        }

        var userByEmail = cdb.FindByUnique<User>("Email", "user2@example.com");
        if (userByEmail != null)
        {
            Console.WriteLine($"✓ 通过邮箱找到用户: {userByEmail.Username} (ID: {userByEmail.Id})");
        }

        // 2. 检查值是否已存在
        bool usernameExists = cdb.IsUniqueValueExists<User>("Username", "admin");
        Console.WriteLine($"✓ 用户名 'admin' 是否存在: {usernameExists}");

        bool emailExists = cdb.IsUniqueValueExists<User>("Email", "newuser@example.com");
        Console.WriteLine($"✓ 邮箱 'newuser@example.com' 是否存在: {emailExists}");

        // 3. 根据复合唯一约束查找员工
        var empFields = new Dictionary<string, object>
        {
            { "CompanyId", 1001L },
            { "EmployeeNumber", "E001" }
        };
        var empByComposite = cdb.FindByCompositeUnique<Employee>("CompanyUnique", empFields);
        if (empByComposite != null)
        {
            Console.WriteLine($"✓ 通过复合约束找到员工: {empByComposite.FullName} (ID: {empByComposite.Id})");
        }
    }

    /// <summary>
    /// 更新操作中的唯一约束示例
    /// </summary>
    public static void UpdateWithUniqueConstraintExample()
    {
        Console.WriteLine("\n=== 更新中的唯一约束示例 ===");

        // 1. 获取现有用户
        var user = cdb.FindByUnique<User>("Username", "user2");
        if (user != null)
        {
            Console.WriteLine($"✓ 找到用户: {user.Username}");

            // 2. 正常更新（不涉及唯一字段）
            user.DisplayName = "更新后的显示名";
            user.Status = "Updated";
            var result = cdb.Update(user);
            Console.WriteLine($"✓ 普通字段更新成功");

            try
            {
                // 3. 尝试更新为已存在的用户名 - 失败
                user.Username = "admin"; // 已存在的用户名
                cdb.Update(user);
                Console.WriteLine("❌ 这行不应该执行到");
            }
            catch (UniqueConstraintViolationException ex)
            {
                Console.WriteLine($"✓ 正确阻止了重复用户名更新: {ex.Message}");
                // 恢复原值
                user.Username = "user2";
            }

            // 4. 更新为新的唯一值 - 成功
            user.PhoneNumber = "13900139000";
            cdb.Update(user);
            Console.WriteLine($"✓ 唯一字段更新成功");
        }
    }

    /// <summary>
    /// 批量操作中的唯一约束示例
    /// </summary>
    public static void BatchOperationExample()
    {
        Console.WriteLine("\n=== 批量操作中的唯一约束示例 ===");

        var users = new List<User>
        {
            new User
            {
                Username = "batch1",
                Email = "batch1@example.com",
                FirstName = "批量",
                LastName = "用户1",
                BirthDate = new DateTime(1993, 1, 1)
            },
            new User
            {
                Username = "batch2",
                Email = "batch2@example.com",
                FirstName = "批量",
                LastName = "用户2",
                BirthDate = new DateTime(1994, 2, 2)
            },
            new User
            {
                Username = "batch3",
                Email = "batch3@example.com",
                FirstName = "批量",
                LastName = "用户3",
                BirthDate = new DateTime(1995, 3, 3)
            }
        };

        try
        {
            cdb.InsertBatch(users);
            Console.WriteLine($"✓ 批量插入 {users.Count} 个用户成功");
        }
        catch (UniqueConstraintViolationException ex)
        {
            Console.WriteLine($"❌ 批量插入失败: {ex.Message}");
        }

        // 尝试批量插入包含重复值的数据
        var duplicateUsers = new List<User>
        {
            new User
            {
                Username = "batch4",
                Email = "batch4@example.com",
                FirstName = "新",
                LastName = "用户",
                BirthDate = new DateTime(1996, 4, 4)
            },
            new User
            {
                Username = "batch1", // 重复的用户名
                Email = "batch5@example.com",
                FirstName = "重复",
                LastName = "用户",
                BirthDate = new DateTime(1997, 5, 5)
            }
        };

        try
        {
            cdb.InsertBatch(duplicateUsers);
            Console.WriteLine("❌ 这行不应该执行到");
        }
        catch (UniqueConstraintViolationException ex)
        {
            Console.WriteLine($"✓ 正确阻止了包含重复值的批量插入: {ex.Message}");
        }
    }

    /// <summary>
    /// 统计信息示例
    /// </summary>
    public static void StatisticsExample()
    {
        Console.WriteLine("\n=== 唯一约束统计信息 ===");

        var (singleConstraints, compositeConstraints) = cdb.GetUniqueConstraintStatistics();
        Console.WriteLine($"✓ 单字段唯一约束数量: {singleConstraints}");
        Console.WriteLine($"✓ 复合字段唯一约束数量: {compositeConstraints}");

        // 显示所有用户
        var allUsers = cdb.FindAll<User>();
        Console.WriteLine($"✓ 当前用户总数: {allUsers.Count}");
        foreach (var u in allUsers)
        {
            Console.WriteLine($"  - {u.Username} ({u.Email}) [ID: {u.Id}]");
        }

        // 显示所有员工
        var allEmployees = cdb.FindAll<Employee>();
        Console.WriteLine($"✓ 当前员工总数: {allEmployees.Count}");
        foreach (var e in allEmployees)
        {
            Console.WriteLine($"  - {e.FullName} (公司: {e.CompanyId}, 工号: {e.EmployeeNumber}) [ID: {e.Id}]");
        }
    }

    /// <summary>
    /// 运行所有示例
    /// </summary>
    public static void RunAllExamples()
    {
        Console.WriteLine("开始执行唯一约束功能演示...\n");

        try
        {
            BasicUniqueConstraintExample();
            CompositeUniqueConstraintExample();
            UniqueConstraintQueryExample();
            UpdateWithUniqueConstraintExample();
            BatchOperationExample();
            StatisticsExample();

            Console.WriteLine("\n🎉 所有示例执行完成！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ 执行过程中发生错误: {ex.Message}");
            Console.WriteLine($"详细信息: {ex}");
        }
    }
}