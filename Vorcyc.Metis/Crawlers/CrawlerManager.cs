using Microsoft.Extensions.Logging;
using Vorcyc.Metis.Services;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.Crawlers;

internal class CrawlerManager
{

    private List<ICrawler> _crawlers =
    [
        new ToutiaoCrawler(),
        new NeteaseCrawler(),
    ];


    public List<ICrawler> Crawlers => _crawlers;



    public void InitializeAll()
    {
        foreach (var crawler in _crawlers)
        {
            crawler.InitializeComponents();
        }
    }


    public void InitializeCrawler(string internalName)
    {
        var crawler = _crawlers.Find(c => c.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
        if (crawler is not null)
        {
            crawler.InitializeComponents();
        }
    }


    public void ReleaseCrawler(string internalName)
    {
        var crawler = _crawlers.Find(c => c.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
        if (crawler is not null)
        {
            crawler.ReleaseComponents();
        }
    }



    public void ReleaseAll()
    {
        foreach (var crawler in _crawlers)
        {
            crawler.ReleaseComponents();
        }
    }



    public async Task RunAllAsync(SQLiteDbContext dbContext, ILogger<CrawlingStorageService> logger, CancellationToken stoppingToken)
    {
        foreach (var crawler in _crawlers)
        {
            await crawler.RunAsync(dbContext, logger, stoppingToken);
        }
    }


    private static CrawlerManager? s_instance = null;

    public static CrawlerManager Current
    {
        get
        {
            s_instance ??= new CrawlerManager();
            return s_instance;
        }
    }

}
