using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vorcyc.Metis.CrawlerPrimitives.Crawlers;

namespace Vorcyc.Metis.CrawlerPrimitives.Services;


public class CrawlingStorageService : BackgroundService
{

    private readonly ILogger<CrawlingStorageService> _logger;

    public CrawlingStorageService(ILogger<CrawlingStorageService> logger)
    {
        _logger = logger;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        await CrawlerManager.Current.InitializeAllAsync();

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[抓取和存储]后台任务运行中...");

            await CrawlerManager.Current.RunAllAsync(_logger, stoppingToken);

            try
            {
                // Wait for next tick; returns false if cancellation requested
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            finally { }

        }
    }


    public override void Dispose()
    {
        CrawlerManager.Current.Dispose();
        base.Dispose();
    }

}