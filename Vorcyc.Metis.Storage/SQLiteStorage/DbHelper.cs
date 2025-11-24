using Microsoft.EntityFrameworkCore;

namespace Vorcyc.Metis.Storage.SQLiteStorage;

public static class DbHelper
{

    public static ArchiveEntity[] GetLast(int count = 20)
    {
        using var db = new SQLiteDbContext();


        var results = db.Archives
                        .OrderByDescending(a => a.PublishTime)
                        .Take(count)
                        .ToArray();

        return results;
    }


    public static ArchiveEntity? GetRandomExcept(IEnumerable<ArchiveEntity> history)
    {
        using var db = new SQLiteDbContext();

        var totalCount = db.Archives.Count();
        if (totalCount <= 1)
        {
            return null!;
        }

        var results = db.Archives
                           .Where(a => !history.Select(h => h.Id).Contains(a.Id))
                           .OrderBy(r => EF.Functions.Random())
                           .FirstOrDefault();
        return results;
    }

    public static IEnumerable<ArchiveEntity> GetRandomBatchExcept(IEnumerable<ArchiveEntity> history, int lessThanDays = 7, int count = 10)
    {
        using var db = new SQLiteDbContext();

        // Build exclusion set from history for efficient lookups
        var excludeIds = history?.Select(h => h.Id).ToHashSet() ?? new HashSet<long>();

        // Define a cutoff to avoid selecting very recent entries
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(0, lessThanDays));

        // Build candidate query
        var candidatesQuery = db.Archives
                                .Where(a => !excludeIds.Contains(a.Id))
                                .Where(a => a.PublishTime <= cutoff);

        // If there aren't enough candidates, return empty
        var candidateCount = candidatesQuery.Count();
        if (candidateCount < count || count <= 0)
        {
            return Enumerable.Empty<ArchiveEntity>();
        }

        // Randomly pick the requested count
        var results = candidatesQuery
                        .OrderBy(a => EF.Functions.Random())
                        .Take(count)
                        .ToArray();

        return results;
    }

}
