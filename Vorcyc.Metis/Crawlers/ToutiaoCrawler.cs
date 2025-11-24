using Microsoft.Extensions.Logging;
using Vorcyc.Metis.CrawlerPrimitives.LinkExtractors;
using Vorcyc.Metis.CrawlerPrimitives.PageContentArchivers;
using Vorcyc.Metis.Services;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.Crawlers;

internal class ToutiaoCrawler : ICrawler
{


    public string Url => "https://www.toutiao.com";

    public string FriendlyName => "今日头条";

    public string InternalName => "toutiao";



    private ToutiaoLinkExtractor? _toutiaoLinkExtractor;

    private ToutiaoPageContentArchiver? _toutiaoPageContentArchiver;

    private bool _isInitialized = false;

    public void InitializeComponents()
    {
        _toutiaoLinkExtractor = new ToutiaoLinkExtractor();
        _toutiaoPageContentArchiver = new ToutiaoPageContentArchiver();
        _isInitialized = true;
    }


    public async Task RunAsync(SQLiteDbContext dbContext, ILogger<CrawlingStorageService> logger, CancellationToken stoppingToken)
    {

        if (!_isInitialized) return;


        var (status, links) = await _toutiaoLinkExtractor.GetPageLinksAndTitlesAsync(10);

        logger.LogWarning(new string('-', 50));
        logger.LogWarning("[抓取和存储]-----头条链接提取状态: {Status}", status);

        if (links is null || links.Length == 0)
        {
            logger.LogInformation("[抓取和存储][头条] 未获取到任何链接");
            return;
        }

        // Step 1: Load existing URLs into a HashSet for fast lookups.
        var existingUrls = new HashSet<string>(
            dbContext.Archives
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
            logger.LogInformation("[抓取和存储][头条] 全部链接已存在，无需归档");
            await _toutiaoLinkExtractor.RefreshAsync();
            return;
        }

        logger.LogInformation("[抓取和存储][头条] 需归档新链接数量: {Count}", newLinks.Length);

        // Step 3: Archive only new links.
        var results = await _toutiaoPageContentArchiver.ArchiveAsync(newLinks, cancellationToken: stoppingToken);
        if (results is null || results.Count == 0)
        {
            logger.LogWarning("[抓取和存储][头条] 归档结果为空");
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

            dbContext.Archives.Add(entity);
            existingUrls.Add(result.Url); // keep set in sync (optional)
        }

        dbContext.SaveChanges();

        await _toutiaoLinkExtractor.RefreshAsync();
    }


    public void ReleaseComponents()
    {
        // No-op
        _toutiaoLinkExtractor?.Dispose();
        _toutiaoPageContentArchiver?.Dispose();
        _toutiaoLinkExtractor = null;
        _toutiaoPageContentArchiver = null;
    }


    public void Dispose()
    {
        this.ReleaseComponents();
    }

}
