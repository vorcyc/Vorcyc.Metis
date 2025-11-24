using Microsoft.Extensions.Logging;
using Vorcyc.Metis.Services;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.Crawlers;

internal interface ICrawler : IDisposable
{

    string Url { get; }

    string FriendlyName { get; }

    string InternalName { get; }


    void InitializeComponents();

    void ReleaseComponents();


    Task RunAsync(SQLiteDbContext dbContext, ILogger<CrawlingStorageService> logger, CancellationToken stoppingToken);

}
