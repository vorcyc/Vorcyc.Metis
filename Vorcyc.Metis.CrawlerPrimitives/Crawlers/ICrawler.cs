using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Text;
using Vorcyc.Metis.CrawlerPrimitives.Services;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis.CrawlerPrimitives.Crawlers;

/// <summary>
/// 爬虫接口：定义爬虫的基本生命周期（初始化、执行、释放）。
/// 所有具体爬虫（如头条、网易）均实现该接口。
/// </summary>
internal interface ICrawler : IDisposable
{
    /// <summary>
    /// 爬虫目标站点的首页 URL。
    /// </summary>
    string Url { get; }

    /// <summary>
    /// 用于日志/UI 显示的友好名称（如"今日头条"、"网易"）。
    /// </summary>
    string FriendlyName { get; }

    /// <summary>
    /// 内部标识名称（小写英文，用于程序内查找和配置匹配）。
    /// </summary>
    string InternalName { get; }

    /// <summary>
    /// 使用共享浏览器实例初始化爬虫内部组件（链接提取器、内容归档器等）。
    /// </summary>
    /// <param name="sharedBrowser">由 <see cref="CrawlerManager"/> 管理的共享浏览器实例。</param>
    void InitializeComponents(IBrowser sharedBrowser);

    /// <summary>
    /// 释放爬虫内部组件，回收浏览器页面等资源。
    /// </summary>
    void ReleaseComponents();

    /// <summary>
    /// 执行一次完整的爬取流程：提取链接 → 归档内容 → 写入数据库。
    /// </summary>
    /// <param name="dbContext">用于写入归档结果的数据库上下文。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="stoppingToken">取消令牌，用于在宿主停止时中断操作。</param>
    Task RunAsync(SQLiteDbContext dbContext, ILogger<CrawlingStorageService> logger, CancellationToken stoppingToken);
}
