using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vorcyc.Metis.Crawlers;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.Services;


public class CrawlingStorageService : BackgroundService
{

    private readonly ILogger<CrawlingStorageService> _logger;

    //private ToutiaoCrawler _toutiao;
    //private NeteaseCrawler _netease;

    private SQLiteDbContext _db;

    public CrawlingStorageService(ILogger<CrawlingStorageService> logger)
    {
        _logger = logger;
        //_toutiao = new ToutiaoCrawler();
        //_netease = new NeteaseCrawler();
        CrawlerManager.Current.InitializeAll();

        _db = new SQLiteDbContext();
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[抓取和存储]后台任务运行中...");


            //await _toutiao.RunAsync(_db, _logger, stoppingToken);
            //await _netease.RunAsync(_db, _logger, stoppingToken);

            await CrawlerManager.Current.RunAllAsync(_db, _logger, stoppingToken);


            // Wait for next tick; returns false if cancellation requested
            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }


    public override void Dispose()
    {
        //_toutiao?.Dispose();
        //_netease?.Dispose();
        CrawlerManager.Current.ReleaseAll();

        _db.Dispose();
        base.Dispose();
    }
}
