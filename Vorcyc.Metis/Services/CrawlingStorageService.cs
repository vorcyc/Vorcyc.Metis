using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vorcyc.Metis.Crawler.LinkExtractors;
using Vorcyc.Metis.Crawler.PageContentArchivers;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.Services;


public class CrawlingStorageService : BackgroundService
{

    private readonly ILogger<CrawlingStorageService> _logger;

    private ToutiaoLinkExtractor _toutiaoLinkExtractor;

    private NeteaseLinkExtractor _neteaseLinkExtractor;

    private ToutiaoPageContentArchiver _toutiaoPageContentArchiver;

    private NeteasePageContentArchiver _neteasePageContentArchiver;

    private SQLiteDbContext _db;

    public CrawlingStorageService(ILogger<CrawlingStorageService> logger)
    {
        _logger = logger;

        _toutiaoLinkExtractor = new();
        _neteaseLinkExtractor = new();
        _toutiaoPageContentArchiver = new();
        _neteasePageContentArchiver = new();
        _db = new SQLiteDbContext();
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[抓取和存储]后台任务运行中...");

            await Toutiao(stoppingToken);
            await Netease(stoppingToken);

            // Wait for next tick; returns false if cancellation requested
            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }


    private async Task Toutiao(CancellationToken stoppingToken)
    {
        var (status, links) = await _toutiaoLinkExtractor.GetPageLinksAndTitlesAsync(10);

        _logger.LogWarning(new string('-', 50));
        _logger.LogWarning("[抓取和存储]-----头条链接提取状态: {Status}", status);

        if (links is null || links.Length == 0)
        {
            _logger.LogInformation("[抓取和存储][头条] 未获取到任何链接");
            return;
        }

        // Step 1: Load existing URLs into a HashSet for fast lookups.
        var existingUrls = new HashSet<string>(
            _db.Archives
               .Select(a => a.Url)
               .Where(u => u != null),
            StringComparer.Ordinal);

        // Step 2: Filter links to only those not yet archived.
        var newLinks = links
            .Where(l => !string.IsNullOrWhiteSpace(l.Url))
            .Where(l => !existingUrls.Contains(l.Url!))
            .ToArray();

        if (newLinks.Length == 0)
        {
            _logger.LogInformation("[抓取和存储][头条] 全部链接已存在，无需归档");
            await _toutiaoLinkExtractor.RefreshAsync();
            return;
        }

        _logger.LogInformation("[抓取和存储][头条] 需归档新链接数量: {Count}", newLinks.Length);

        // Step 3: Archive only new links.
        var results = await _toutiaoPageContentArchiver.ArchiveAsync(newLinks, cancellationToken: stoppingToken);
        if (results is null || results.Count == 0)
        {
            _logger.LogWarning("[抓取和存储][头条] 归档结果为空");
            await _toutiaoLinkExtractor.RefreshAsync();
            return;
        }

        // Step 4: Persist (defensive re-check in case of race conditions).
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Url)) continue;
            if (existingUrls.Contains(result.Url)) continue;
            if (result.TextLength == 0) continue;

            var entity = new Vorcyc.Metis.Storage.SQLiteStorage.ArchiveEntity
            {
                Title = result.Title ?? string.Empty,
                Url = result.Url,
                ImageCount = result.ImageCount,
                TextLength = result.TextLength,
                Publisher = result.Publisher,
                PublishTime = result.PublishTime,
                Content = result.Content ?? string.Empty,
                Category = Vorcyc.Metis.Classifiers.Text.PageCategoryBuilder.Build(result.Title)
            };

            _db.Archives.Add(entity);
            existingUrls.Add(result.Url); // keep set in sync (optional)
        }

        _db.SaveChanges();

        await _toutiaoLinkExtractor.RefreshAsync();
    }



    private async Task Netease(CancellationToken stoppingToken)
    {
        var (status, links) = await _neteaseLinkExtractor.GetPageLinksAndTitlesAsync();

        _logger.LogWarning(new string('-', 50));
        _logger.LogWarning("[抓取和存储]-----网易链接提取状态: {Status}", status);

        if (links is null || links.Length == 0)
        {
            _logger.LogInformation("[抓取和存储][网易] 未获取到任何链接");
            return;
        }

        // 1) 一次性加载已存在的 URL，便于快速排重
        var existingUrls = new HashSet<string>(
            _db.Archives
               .Select(a => a.Url)
               .Where(u => u != null),
            StringComparer.Ordinal);

        // 2) 过滤出数据库中不存在的新链接
        var newLinks = links
            .Where(l => !string.IsNullOrWhiteSpace(l.Url))
            .Where(l => !existingUrls.Contains(l.Url!))
            .ToArray();

        if (newLinks.Length == 0)
        {
            _logger.LogInformation("[抓取和存储][网易] 全部链接已存在，无需归档");
            return;
        }

        _logger.LogInformation("[抓取和存储][网易] 需归档新链接数量: {Count}", newLinks.Length);

        // 3) 仅对新链接执行归档
        var results = await _neteasePageContentArchiver.ArchiveAsync(newLinks, cancellationToken: stoppingToken);
        if (results is null || results.Count == 0)
        {
            _logger.LogWarning("[抓取和存储][网易] 归档结果为空");
            return;
        }

        // 4) 写入数据库（再次防御性检查，避免竞态写入）
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Url)) continue;
            if (existingUrls.Contains(result.Url)) continue;
            if (result.TextLength == 0) continue;

            _db.Archives.Add(new Vorcyc.Metis.Storage.SQLiteStorage.ArchiveEntity
            {
                Title = result.Title ?? string.Empty,
                Url = result.Url,
                ImageCount = result.ImageCount,
                TextLength = result.TextLength,
                Publisher = result.Publisher,
                PublishTime = result.PublishTime,
                Content = result.Content ?? string.Empty,
                Category = Vorcyc.Metis.Classifiers.Text.PageCategoryBuilder.Build(result.Title)
            });

            existingUrls.Add(result.Url);
        }

        _db.SaveChanges();
    }






    public override void Dispose()
    {
        _toutiaoLinkExtractor.Dispose();
        _neteaseLinkExtractor.Dispose();
        _toutiaoPageContentArchiver.Dispose();
        _neteasePageContentArchiver.Dispose();
        _db.Dispose();

        base.Dispose();
    }
}
