using Microsoft.EntityFrameworkCore;
using Vorcyc.Metis.Classifiers.Text;

namespace Vorcyc.Metis.Storage.SQLiteStorage;

/// <summary>
/// 数据访问辅助工具类，封装常用的随机与最近项查询逻辑。
/// 依赖 <see cref="SQLiteDbContext"/> 与实体 <see cref="ArchiveEntity"/>。
/// </summary>
public static class DbHelper
{
    /// <summary>
    /// 获取按照发布时间倒序排列的最近若干条归档记录。
    /// </summary>
    /// <param name="count">返回的最大条目数，默认 5。</param>
    /// <returns>最近的若干 <see cref="ArchiveEntity"/> 项目。</returns>
    public static ArchiveEntity[] GetLast(int count = 5)
    {
        using var db = new SQLiteDbContext();

        var results = db.Archives
                        .OrderByDescending(a => a.PublishTime) // 按发布时间倒序
                        .Take(count)                           // 限制条数
                        .ToArray();

        return results;
    }

    /// <summary>
    /// 获取一个随机的归档记录，排除传入的历史集合。
    /// 当分类下项目不足时用于兜底选择。
    /// </summary>
    /// <param name="history">需要排除的历史项目集合（根据 <see cref="ArchiveEntity.Id"/> 排除）。</param>
    /// <returns>随机的 <see cref="ArchiveEntity"/>；若候选不足则返回 <c>null</c>。</returns>
    public static ArchiveEntity? GetRandomExcept(IEnumerable<ArchiveEntity> history)
    {
        using var db = new SQLiteDbContext();

        var totalCount = db.Archives.Count();
        if (totalCount <= 1)
        {
            // 如果库中项目过少，则直接返回 null 以避免重复
            return null!;
        }

        var results = db.Archives
                        .Where(a => !history.Select(h => h.Id).Contains(a.Id)) // 排除历史
                        .OrderBy(r => EF.Functions.Random())                   // 随机排序
                        .FirstOrDefault();
        return results;
    }

    /// <summary>
    /// 在指定分类下获取一个随机的归档记录，排除传入的历史集合。
    /// </summary>
    /// <param name="history">需要排除的历史项目集合（根据 <see cref="ArchiveEntity.Id"/> 排除）。</param>
    /// <param name="category">
    /// 分类标志（<see cref="PageContentCategory"/>）。若为 <see cref="PageContentCategory.All"/>，
    /// 则匹配任意非 <see cref="PageContentCategory.None"/> 的记录；否则使用按位匹配（任意位命中）。
    /// </param>
    /// <returns>随机的 <see cref="ArchiveEntity"/>；若候选不足则返回 <c>null</c>。</returns>
    public static ArchiveEntity? GetRandomExcept(IEnumerable<ArchiveEntity> history, PageContentCategory category)
    {
        using var db = new SQLiteDbContext();

        var excludeIds = history?.Select(h => h.Id).ToHashSet() ?? new HashSet<long>();

        IQueryable<ArchiveEntity> matchesQuery;

        // 当 category 为 All 时，表示接受任何已分类的记录（非 None）
        if (category == PageContentCategory.All)
        {
            matchesQuery = db.Archives
                             .Where(a => a.CategoryValue != PageContentCategory.None);
        }
        else
        {
            // 任意位匹配：记录包含传入分类的任意一个标志位
            // 注：EF Core 在 SQLite 下可稳定翻译该位运算；如遇到问题可改为强制整数转换。
            matchesQuery = db.Archives
                             .Where(a => (a.CategoryValue & category) != 0);
        }

        var totalCount = matchesQuery.Count();
        if (totalCount <= 1)
        {
            // 候选过少则返回 null，避免返回无意义结果
            return null!;
        }

        var result = matchesQuery
                        .Where(a => !excludeIds.Contains(a.Id)) // 排除历史
                        .OrderBy(r => EF.Functions.Random())     // 随机挑选
                        .FirstOrDefault();

        return result;
    }

    /// <summary>
    /// 在指定分类下获取最近 <paramref name="lessThanDays"/> 天内的随机批次，排除传入的历史集合。
    /// 返回数量不超过 <paramref name="maxCount"/>；若不足则返回实际可用的数量。
    /// </summary>
    /// <param name="history">需要排除的历史项目集合（根据 <see cref="ArchiveEntity.Id"/> 排除）。</param>
    /// <param name="category">
    /// 分类标志（<see cref="PageContentCategory"/>）。若为 <see cref="PageContentCategory.All"/>，
    /// 则匹配任意非 <see cref="PageContentCategory.None"/> 的记录；否则使用按位匹配（任意位命中）。
    /// </param>
    /// <param name="lessThanDays">时间窗口（天）。仅选择最近 N 天内（包含当天）的项目，默认 7 天。</param>
    /// <param name="maxCount">最多返回的条目数，默认 10。若候选不足则返回较少的条目。</param>
    /// <returns>随机的 <see cref="ArchiveEntity"/> 批次，数量 ≤ <paramref name="maxCount"/>。</returns>
    public static IEnumerable<ArchiveEntity> GetRandomBatchExcept(IEnumerable<ArchiveEntity> history, PageContentCategory category, int lessThanDays = 7, int maxCount = 10)
    {
        using var db = new SQLiteDbContext();

        // 排除历史 ID，避免重复选择
        var excludeIds = history?.Select(h => h.Id).ToHashSet() ?? new HashSet<long>();

        // 使用 DateTimeOffset.UtcNow 与存储的 UTC 值一致，选择最近 N 天的项目（发布 >= cutoffUtc）
        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(0, lessThanDays));

        // 先按时间窗口与历史排除过滤，再施加分类过滤
        IQueryable<ArchiveEntity> candidatesQuery = db.Archives
                                                      .Where(a => !excludeIds.Contains(a.Id))
                                                      .Where(a => a.PublishTime >= cutoffUtc);

        if (category == PageContentCategory.All)
        {
            // All：匹配任何非 None 的记录
            candidatesQuery = candidatesQuery.Where(a => a.CategoryValue != PageContentCategory.None);
        }
        else
        {
            // 任意位命中
            candidatesQuery = candidatesQuery.Where(a => (a.CategoryValue & category) != 0);
            // 如需“包含所有位”，可替换为：
            // candidatesQuery = candidatesQuery.Where(a => (a.CategoryValue & category) == category);
        }

        var candidateCount = candidatesQuery.Count();
        if (candidateCount <= 0 || maxCount <= 0)
        {
            // 候选为空或请求数非法，返回空序列
            return Enumerable.Empty<ArchiveEntity>();
        }

        // 最多取 maxCount 条，若不足则取实际数量
        var take = Math.Min(maxCount, candidateCount);

        var results = candidatesQuery
                        .OrderBy(a => EF.Functions.Random()) // 随机排序
                        .Take(take)
                        .ToArray();

        return results;
    }

    /// <summary>
    /// 在最近 <paramref name="lessThanDays"/> 天内获取随机批次（不限定分类），排除传入的历史集合。
    /// 返回数量不超过 <paramref name="maxCount"/>；若不足则返回实际可用的数量。
    /// </summary>
    /// <param name="history">需要排除的历史项目集合（根据 <see cref="ArchiveEntity.Id"/> 排除）。</param>
    /// <param name="lessThanDays">时间窗口（天）。仅选择最近 N 天内（包含当天）的项目，默认 7 天。</param>
    /// <param name="maxCount">最多返回的条目数，默认 10。若候选不足则返回较少的条目。</param>
    /// <returns>随机的 <see cref="ArchiveEntity"/> 批次，数量 ≤ <paramref name="maxCount"/>。</returns>
    public static IEnumerable<ArchiveEntity> GetRandomBatchExcept(IEnumerable<ArchiveEntity> history, int lessThanDays = 7, int maxCount = 10)
    {
        using var db = new SQLiteDbContext();

        // 排除历史 ID，避免重复选择
        var excludeIds = history?.Select(h => h.Id).ToHashSet() ?? new HashSet<long>();

        // 使用 UTC DateTimeOffset 进行时间窗口过滤（发布 >= cutoffUtc）
        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(0, lessThanDays));

        IQueryable<ArchiveEntity> candidatesQuery = db.Archives
                                                      .Where(a => !excludeIds.Contains(a.Id))
                                                      .Where(a => a.PublishTime >= cutoffUtc);

        var candidateCount = candidatesQuery.Count();
        if (candidateCount <= 0 || maxCount <= 0)
        {
            // 候选为空或请求数非法，返回空序列
            return Enumerable.Empty<ArchiveEntity>();
        }

        // 最多取 maxCount 条，若不足则取实际数量
        var take = Math.Min(maxCount, candidateCount);

        var results = candidatesQuery
                        .OrderBy(a => EF.Functions.Random()) // 随机排序
                        .Take(take)
                        .ToArray();

        return results;
    }
}