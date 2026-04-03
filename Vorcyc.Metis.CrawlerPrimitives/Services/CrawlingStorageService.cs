using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vorcyc.Metis.CrawlerPrimitives.Crawlers;

namespace Vorcyc.Metis.CrawlerPrimitives.Services;


/// <summary>
/// 后台爬取与存储服务：定时执行所有爬虫的抓取流程并将结果写入数据库。
/// 作为 <see cref="BackgroundService"/> 注册到宿主中，默认每 10 分钟运行一次。
/// </summary>
public class CrawlingStorageService : BackgroundService
{
    /// <summary>日志记录器。</summary>
    private readonly ILogger<CrawlingStorageService> _logger;

    /// <summary>
    /// 初始化爬取服务实例。
    /// </summary>
    /// <param name="logger">日志记录器，由依赖注入提供。</param>
    public CrawlingStorageService(ILogger<CrawlingStorageService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 后台执行入口：初始化爬虫管理器，然后每隔 10 分钟执行一次全量爬取。
    /// </summary>
    /// <param name="stoppingToken">宿主停止时触发的取消令牌。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        await CrawlerManager.Current.InitializeAllAsync();

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[抓取和存储]后台任务运行中...");

            await CrawlerManager.Current.RunAllAsync(_logger, stoppingToken);

            // 等待下一个周期；取消时返回 false 以退出循环
            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }

    /// <summary>
    /// 释放爬虫管理器持有的浏览器与数据库资源。
    /// </summary>
    public override void Dispose()
    {
        CrawlerManager.Current.Dispose();
        base.Dispose();
    }

}