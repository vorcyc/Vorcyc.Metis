using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Text;
using Vorcyc.Metis.CrawlerPrimitives.Services;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.CrawlerPrimitives.Crawlers;

internal interface ICrawler : IDisposable
{


    string Url { get; }

    string FriendlyName { get; }

    string InternalName { get; }


    void InitializeComponents(IBrowser sharedBrowser);

    void ReleaseComponents();


    Task RunAsync(SQLiteDbContext dbContext, ILogger<CrawlingStorageService> logger, CancellationToken stoppingToken);



}
