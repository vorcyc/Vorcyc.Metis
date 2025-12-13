using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using Vorcyc.Metis.CrawlerPrimitives.Services;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.CrawlerPrimitives.Crawlers;

internal class CrawlerManager : IDisposable
{

    private List<ICrawler> _crawlers =
    [
        new ToutiaoCrawler(),
        new NeteaseCrawler(),
    ];



    private IBrowser? _sharedBrowser = null;
    private SQLiteDbContext? _db = null;


    public List<ICrawler> Crawlers => _crawlers;



    public async Task InitializeAllAsync()
    {
        // 启动无头浏览器；禁用 sandbox 适配容器/CI 环境；禁用 /dev/shm 限制以缓解共享内存不足
        _sharedBrowser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-dev-shm-usage"]
        });

        _db = new SQLiteDbContext();

        foreach (var crawler in _crawlers)
        {
            crawler.InitializeComponents(_sharedBrowser);
        }
    }


    public void InitializeCrawler(string internalName)
    {
        var crawler = _crawlers.Find(c => c.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
        if (crawler is not null)
        {
            crawler.InitializeComponents(_sharedBrowser);
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






    public async Task RunAllAsync(ILogger<CrawlingStorageService> logger, CancellationToken stoppingToken)
    {
        foreach (var crawler in _crawlers)
        {
            await crawler.RunAsync(_db, logger, stoppingToken);
        }
    }

    public void ReleaseAll()
    {
        foreach (var crawler in _crawlers)
        {
            crawler.ReleaseComponents();
        }
    }



    public void Dispose()
    {
        ReleaseAll();
        _sharedBrowser?.Dispose();
        _sharedBrowser = null;
        _db?.Dispose();
        _db = null;
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
