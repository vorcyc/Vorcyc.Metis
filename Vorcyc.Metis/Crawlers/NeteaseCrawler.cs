using Microsoft.Extensions.Logging;
using Vorcyc.Metis.CrawlerPrimitives.LinkExtractors;
using Vorcyc.Metis.CrawlerPrimitives.PageContentArchivers;
using Vorcyc.Metis.Services;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.Crawlers;

internal class NeteaseCrawler : ICrawler
{

    public string Url => "https://www.163.com";

    public string FriendlyName => "网易";

    public string InternalName => "netease";


    private NeteaseLinkExtractor? _neteaseLinkExtractor;

    private NeteasePageContentArchiver? _neteasePageContentArchiver;


    public void InitializeComponents()
    {
        _neteaseLinkExtractor = new();
        _neteasePageContentArchiver = new();
    }

    public async Task RunAsync(SQLiteDbContext dbContext, ILogger<CrawlingStorageService> logger, CancellationToken stoppingToken)
    {
        var (status, links) = await _neteaseLinkExtractor.GetPageLinksAndTitlesAsync();

        logger.LogWarning(new string('-', 50));
        logger.LogWarning("[抓取和存储]-----网易链接提取状态: {Status}", status);

        if (links is null || links.Length == 0)
        {
            logger.LogInformation("[抓取和存储][网易] 未获取到任何链接");
            return;
        }

        // 1) 一次性加载已存在的 URL，便于快速排重
        var existingUrls = new HashSet<string>(
            dbContext.Archives
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
            logger.LogInformation("[抓取和存储][网易] 全部链接已存在，无需归档");
            return;
        }

        logger.LogInformation("[抓取和存储][网易] 需归档新链接数量: {Count}", newLinks.Length);

        // 3) 仅对新链接执行归档
        var results = await _neteasePageContentArchiver.ArchiveAsync(newLinks, cancellationToken: stoppingToken);
        if (results is null || results.Count == 0)
        {
            logger.LogWarning("[抓取和存储][网易] 归档结果为空");
            return;
        }

        // 4) 写入数据库（再次防御性检查，避免竞态写入）
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Url)) continue;
            if (existingUrls.Contains(result.Url)) continue;
            if (result.TextLength == 0) continue;

            dbContext.Archives.Add(new Vorcyc.Metis.Storage.SQLiteStorage.ArchiveEntity
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

        dbContext.SaveChanges();
    }


    public void ReleaseComponents()
    {
        _neteaseLinkExtractor?.Dispose();
        _neteasePageContentArchiver?.Dispose();
        _neteaseLinkExtractor = null;
        _neteasePageContentArchiver = null;
    }

    public void Dispose()
    {
        this.ReleaseComponents();
    }

}
