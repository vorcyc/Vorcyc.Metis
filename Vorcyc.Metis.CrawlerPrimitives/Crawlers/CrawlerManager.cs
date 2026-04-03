using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using Vorcyc.Metis.CrawlerPrimitives.Services;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.CrawlerPrimitives.Crawlers;

/// <summary>
/// 爬虫管理器：统一管理所有爬虫的生命周期（初始化、执行、释放），
/// 并维护共享的无头浏览器实例与数据库上下文。
/// </summary>
/// <remarks>
/// 通过 <see cref="Current"/> 获取单例实例；
/// 调用 <see cref="InitializeAllAsync"/> 初始化浏览器与所有爬虫；
/// 调用 <see cref="RunAllAsync"/> 依次执行所有爬虫；
/// 程序退出时调用 <see cref="Dispose"/> 释放资源。
/// </remarks>
internal class CrawlerManager : IDisposable
{
    /// <summary>
    /// 已注册的爬虫列表。
    /// </summary>
    private readonly List<ICrawler> _crawlers =
    [
        new ToutiaoCrawler(),
        new NeteaseCrawler(),
    ];

    /// <summary>
    /// 所有爬虫共享的无头浏览器实例。
    /// </summary>
    private IBrowser? _sharedBrowser = null;

    /// <summary>
    /// 共享的 SQLite 数据库上下文。
    /// </summary>
    private SQLiteDbContext? _db = null;

    /// <summary>
    /// 获取已注册的爬虫列表（只读访问）。
    /// </summary>
    public List<ICrawler> Crawlers => _crawlers;

    /// <summary>
    /// 异步初始化所有爬虫：启动共享无头浏览器、创建数据库上下文，并为每个爬虫注入浏览器实例。
    /// </summary>
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

    /// <summary>
    /// 按内部名称初始化指定爬虫。
    /// </summary>
    /// <param name="internalName">爬虫的内部标识名称（不区分大小写）。</param>
    public void InitializeCrawler(string internalName)
    {
        var crawler = _crawlers.Find(c => c.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
        if (crawler is not null)
        {
            crawler.InitializeComponents(_sharedBrowser);
        }
    }

    /// <summary>
    /// 按内部名称释放指定爬虫的资源。
    /// </summary>
    /// <param name="internalName">爬虫的内部标识名称（不区分大小写）。</param>
    public void ReleaseCrawler(string internalName)
    {
        var crawler = _crawlers.Find(c => c.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase));
        if (crawler is not null)
        {
            crawler.ReleaseComponents();
        }
    }

    /// <summary>
    /// 依次执行所有爬虫的抓取流程。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="stoppingToken">取消令牌。</param>
    public async Task RunAllAsync(ILogger<CrawlingStorageService> logger, CancellationToken stoppingToken)
    {
        foreach (var crawler in _crawlers)
        {
            await crawler.RunAsync(_db, logger, stoppingToken);
        }
    }

    /// <summary>
    /// 释放所有爬虫的内部组件。
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var crawler in _crawlers)
        {
            crawler.ReleaseComponents();
        }
    }

    /// <summary>
    /// 释放所有资源：释放爬虫组件、关闭浏览器、释放数据库上下文。
    /// </summary>
    public void Dispose()
    {
        ReleaseAll();
        _sharedBrowser?.Dispose();
        _sharedBrowser = null;
        _db?.Dispose();
        _db = null;
    }

    private static CrawlerManager? s_instance = null;

    /// <summary>
    /// 爬虫管理器的全局单例实例。
    /// </summary>
    public static CrawlerManager Current
    {
        get
        {
            s_instance ??= new CrawlerManager();
            return s_instance;
        }
    }

}
