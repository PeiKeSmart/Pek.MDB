using System.Collections.Concurrent;
using DH.Data.Cache.TypedIndex;

namespace DH.Data.Cache;

/// <summary>
/// 异步查询扩展 - 支持异步查询操作以提升高并发性能
/// </summary>
public static class AsyncQueryExtensions
{
    /// <summary>
    /// 异步范围查询
    /// </summary>
    /// <typeparam name="T">查询对象类型</typeparam>
    /// <param name="propertyName">属性名称</param>
    /// <param name="minValue">最小值</param>
    /// <param name="maxValue">最大值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询结果</returns>
    public static async Task<List<T>> FindByRangeAsync<T>(
        string propertyName, 
        IComparable minValue, 
        IComparable maxValue,
        CancellationToken cancellationToken = default) where T : CacheObject
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TypedQueryExtensions.FindByRange<T>(propertyName, minValue, maxValue);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步模糊查询
    /// </summary>
    /// <typeparam name="T">查询对象类型</typeparam>
    /// <param name="propertyName">属性名称</param>
    /// <param name="pattern">匹配模式</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询结果</returns>
    public static async Task<List<T>> FindByLikeAsync<T>(
        string propertyName, 
        string pattern,
        CancellationToken cancellationToken = default) where T : CacheObject
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TypedQueryExtensions.FindByLike<T>(propertyName, pattern);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步复合查询
    /// </summary>
    /// <typeparam name="T">查询对象类型</typeparam>
    /// <param name="conditions">查询条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询结果</returns>
    public static async Task<List<T>> FindByMultipleAsync<T>(
        Dictionary<string, object> conditions,
        CancellationToken cancellationToken = default) where T : CacheObject
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TypedQueryExtensions.FindByMultiple<T>(conditions);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步批量查询
    /// </summary>
    /// <typeparam name="T">查询对象类型</typeparam>
    /// <param name="ids">ID列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询结果</returns>
    public static async Task<Dictionary<long, T>> FindByIdsAsync<T>(
        IEnumerable<long> ids,
        CancellationToken cancellationToken = default) where T : CacheObject
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TypedQueryExtensions.FindByIds<T>(ids);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步分页查询
    /// </summary>
    /// <typeparam name="T">查询对象类型</typeparam>
    /// <param name="propertyName">属性名称</param>
    /// <param name="pageIndex">页码（从0开始）</param>
    /// <param name="pageSize">页大小</param>
    /// <param name="ascending">是否升序</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分页结果</returns>
    public static async Task<PagedResult<T>> FindByPageAsync<T>(
        string propertyName, 
        int pageIndex, 
        int pageSize, 
        bool ascending = true,
        CancellationToken cancellationToken = default) where T : CacheObject
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TypedQueryExtensions.FindByPage<T>(propertyName, pageIndex, pageSize, ascending);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步批量插入
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="objects">对象列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>插入结果</returns>
    public static async Task<BatchOperationResult> BatchInsertAsync<T>(
        IEnumerable<T> objects,
        CancellationToken cancellationToken = default) where T : CacheObject
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var result = new BatchOperationResult();
            var objectList = objects.ToList();
            
            try
            {
                foreach (var obj in objectList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        MemoryDB.Insert(obj);
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new BatchOperationError
                        {
                            ObjectId = obj.Id,
                            ErrorMessage = ex.Message
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.IsCanceled = true;
                throw;
            }
            
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步批量更新
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="objects">对象列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新结果</returns>
    public static async Task<BatchOperationResult> BatchUpdateAsync<T>(
        IEnumerable<T> objects,
        CancellationToken cancellationToken = default) where T : CacheObject
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var result = new BatchOperationResult();
            var objectList = objects.ToList();
            
            try
            {
                foreach (var obj in objectList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        MemoryDB.Update(obj);
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new BatchOperationError
                        {
                            ObjectId = obj.Id,
                            ErrorMessage = ex.Message
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.IsCanceled = true;
                throw;
            }
            
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步批量删除
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="ids">ID列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>删除结果</returns>
    public static async Task<BatchOperationResult> BatchDeleteAsync<T>(
        IEnumerable<long> ids,
        CancellationToken cancellationToken = default) where T : CacheObject
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var result = new BatchOperationResult();
            var idList = ids.ToList();
            
            try
            {
                foreach (var id in idList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try
                    {
                        // 需要先找到对象再删除
                        var obj = MemoryDB.FindById(typeof(T), id);
                        if (obj != null)
                        {
                            MemoryDB.Delete((CacheObject)obj);
                        }
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new BatchOperationError
                        {
                            ObjectId = id,
                            ErrorMessage = ex.Message
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.IsCanceled = true;
                throw;
            }
            
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// 批量操作结果
/// </summary>
public class BatchOperationResult
{
    /// <summary>
    /// 成功数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 错误列表
    /// </summary>
    public List<BatchOperationError> Errors { get; set; } = new();

    /// <summary>
    /// 是否被取消
    /// </summary>
    public bool IsCanceled { get; set; }

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount => SuccessCount + Errors.Count;

    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount : 0;

    /// <summary>
    /// 是否完全成功
    /// </summary>
    public bool IsSuccess => Errors.Count == 0 && !IsCanceled;
}

/// <summary>
/// 批量操作错误
/// </summary>
public class BatchOperationError
{
    /// <summary>
    /// 对象ID
    /// </summary>
    public long ObjectId { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;
}
