using PuppeteerSharp;
using System.Text.RegularExpressions;

namespace Vorcyc.Metis.Crawler.LinkExtractors;

/// <summary>
/// 网易（163.com）首页链接提取器（实例版）。
/// </summary>
/// <remarks>
/// 功能与行为：
/// - 启动无头 Chromium（如本地已存在则跳过下载）并打开单一 <see cref="IPage"/>；
/// - 导航至基础地址（默认 https://www.163.com），仅等待 __DOMContentLoaded__；
/// - 在浏览器端收集所有 <c>a[href]</c>，解析为绝对 URL，并仅保留以 <c>https://www.163.com/dy/article/</c>
///   或 <c>https://www.163.com/news/article/</c> 开头的文章类链接；
/// - 对标题执行规范化（折叠所有空白并 Trim），并对结果按 URL 去重；
/// - 所有异常会被捕获并转换为 <see cref="LinkExtractionStatus.Error"/>；若导航正常但无符合条件的链接，返回 <see cref="LinkExtractionStatus.NoLinks"/>。
///
/// 生命周期：
/// - 本类型为有状态实例，内部复用同一个浏览器与页面；
/// - 非线程安全：不要并发调用实例方法；
/// - 使用完毕后请调用 <see cref="Dispose"/> 或 <see cref="DisposeAsync"/> 正确释放资源。
///
/// 示例：
/// <code language="csharp">
/// await using var extractor = new NeteaseLinkExtractor();            // 默认首页
/// var (status, links) = await extractor.GetPageLinksAndTitlesAsync();
/// if (status == LinkExtractionStatus.Success && links is not null)
/// {
///     foreach (var link in links)
///     {
///         Console.WriteLine($"{link.Title} -> {link.Url}");
///     }
/// }
/// </code>
/// </remarks>
public sealed class NeteaseLinkExtractor : IDisposable, IAsyncDisposable
{
    // 基础地址（可通过构造函数自定义）
    private readonly string _baseUrl;

    // 浏览器与页面句柄（Lazy 初始化，实例级复用）
    private IBrowser? _browser;
    private IPage? _page;

    // 释放标记，防止重复释放与使用已释放对象
    private bool _disposed;

    /// <summary>
    /// 使用指定基础地址创建提取器实例。
    /// </summary>
    /// <param name="baseUrl">基础地址，默认值为 https://www.163.com。</param>
    public NeteaseLinkExtractor(string baseUrl = "https://www.163.com")
    {
        _baseUrl = baseUrl;
    }

    /// <summary>
    /// 若实例已释放，抛出 <see cref="ObjectDisposedException"/>。
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NeteaseLinkExtractor));
    }

    /// <summary>
    /// 标题规范化：将所有空白字符折叠为单个空格，并去除首尾空白。
    /// </summary>
    /// <param name="s">原始标题字符串。</param>
    /// <returns>若为空或全空白，返回空字符串；否则返回折叠后的单行字符串。</returns>
    private static string NormalizeTitle(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return Regex.Replace(s.ReplaceLineEndings(" "), @"\s+", " ").Trim();
    }

    /// <summary>
    /// 确保浏览器与页面已就绪（按需初始化）。
    /// </summary>
    /// <remarks>
    /// - 若本地已有兼容的 Chromium，会跳过下载；否则将尝试拉取。<br/>
    /// - 默认设置页面导航超时为 30 秒。<br/>
    /// - 该方法非线程安全：请避免并发访问同一实例。
    /// </remarks>
    private async Task EnsurePageAsync()
    {
        if (_page is not null && _browser is not null) return;

        // 确保 Chromium 存在（已存在则快速返回）
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true // 无头运行，适合服务端/批处理
        });

        _page = await _browser.NewPageAsync();
        _page.DefaultNavigationTimeout = 30_000; // 30 秒导航超时
    }

    /// <summary>
    /// 刷新当前页面。
    /// </summary>
    /// <param name="timeoutMs">导航超时时间（毫秒），默认 30000。</param>
    /// <param name="waitUntil">
    /// 导航完成条件，默认 <see cref="WaitUntilNavigation.DOMContentLoaded"/>。
    /// </param>
    /// <returns>
    /// 刷新是否成功：<c>true</c> 表示刷新/首开成功且响应为 OK；否则为 <c>false</c>。
    /// </returns>
    /// <remarks>
    /// 行为说明：若当前页为 <c>about:blank</c>（尚未导航），将导航到基础地址；否则执行页面重载。内部会吞掉所有异常并返回 <c>false</c>。
    /// </remarks>
    public async Task<bool> RefreshAsync(int timeoutMs = 30_000, WaitUntilNavigation waitUntil = WaitUntilNavigation.DOMContentLoaded)
    {
        ThrowIfDisposed();
        await EnsurePageAsync();

        try
        {
            var navOptions = new NavigationOptions
            {
                WaitUntil = [waitUntil],
                Timeout = timeoutMs
            };

            // 若尚未导航（about:blank），执行首次导航；否则执行 Reload
            var isBlank = string.IsNullOrWhiteSpace(_page!.Url) ||
                          _page.Url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

            var response = isBlank
                ? await _page.GoToAsync(_baseUrl, navOptions)
                : await _page.ReloadAsync(navOptions);

            return response is not null && response.Ok;
        }
        catch
        {
            // 统一降级为 false，避免异常冒泡
            return false;
        }
    }

    /// <summary>
    /// 访问基础地址并提取文章链接（仅保留以 https://www.163.com/dy/article/ 或 https://www.163.com/news/article/ 开头的链接）。
    /// </summary>
    /// <returns>
    /// 元组：
    /// - <see cref="LinkExtractionStatus"/> status：执行状态；<br/>
    /// - <see cref="Link"/>[] anchors：提取到的链接与标题（可能为 <c>null</c>）。
    /// </returns>
    /// <remarks>
    /// 提取流程：
    /// 1) 导航到基础地址，仅等待 DOMContentLoaded；<br/>
    /// 2) 在浏览器端遍历 <c>a[href]</c>，将 href 解析为绝对 URL；<br/>
    /// 3) 仅保留以 <c>https://www.163.com/dy/article/</c> 或 <c>https://www.163.com/news/article/</c> 开头的链接；<br/>
    /// 4) 提取锚点文本为标题，回到本地后进行标题规范化并按 URL 去重；<br/>
    /// 5) 若非空则成功返回；若导航异常或响应非 OK，返回 <see cref="LinkExtractionStatus.NavigationFailed"/>；否则返回 <see cref="LinkExtractionStatus.NoLinks"/>。
    /// </remarks>
    public async Task<(LinkExtractionStatus status, Link[]? anchors)> GetPageLinksAndTitlesAsync()
    {
        ThrowIfDisposed();

        try
        {
            await EnsurePageAsync();

            // 导航到基础地址，仅等待 DOMContentLoaded 即开始提取
            var response = await _page!.GoToAsync(_baseUrl, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 30_000
            });

            var hadNavIssue = response is null || !response.Ok;

            // 在浏览器端仅保留以指定绝对前缀开头的链接：
            // https://www.163.com/dy/article/ 或 https://www.163.com/news/article/
            var items = await _page.EvaluateFunctionAsync<Link[]>(@"
                () => {
                    const out = [];
                    const anchors = document.querySelectorAll('a[href]');
                    for (const a of anchors) {
                        const hrefRaw = (a.getAttribute('href') || a.href || '').trim();
                        if (!hrefRaw) continue;

                        // 将相对/协议相对地址解析为绝对地址
                        let absolute = hrefRaw;
                        try {
                            const resolver = document.createElement('a');
                            resolver.href = hrefRaw;
                            absolute = resolver.href;
                        } catch { continue; }

                        // 只接受以目标前缀开头的 HTTPS 绝对链接
                        if (!(absolute.startsWith('https://www.163.com/dy/article/') ||
                              absolute.startsWith('https://www.163.com/news/article/'))) {
                            continue;
                        }

                        // 取可见文本作为标题（折叠空白）
                        let title = (a.textContent || '').replace(/\s+/g, ' ').trim();
                        if (!title) continue;

                        // 基本 URL 校验
                        try { new URL(absolute); } catch { continue; }

                        out.push({ title, url: absolute });
                    }
                    return out;
                }
            ");

            // 标题规范化 + 按 URL 去重（保留首个出现的项）
            var baseList = (items ?? Array.Empty<Link>())
                .Select(a => new Link
                {
                    Title = NormalizeTitle(a.Title),
                    Url = a.Url
                })
                .Where(a => !string.IsNullOrWhiteSpace(a.Title))
                .Where(a => !string.IsNullOrWhiteSpace(a.Url))
                .GroupBy(a => a.Url!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray();

            // 本地再进行一次防御性前缀校验，确保只返回期望前缀的链接
            var filtered = baseList
                .Where(a => a.Url is { } u &&
                            (u.StartsWith("https://www.163.com/dy/article/", StringComparison.Ordinal) ||
                             u.StartsWith("https://www.163.com/news/article/", StringComparison.Ordinal)))
                .ToArray();

            if (filtered.Length > 0)
            {
                return (LinkExtractionStatus.Success, filtered);
            }

            // 导航异常优先返回 NavigationFailed；否则表示无可用链接
            return (hadNavIssue ? LinkExtractionStatus.NavigationFailed : LinkExtractionStatus.NoLinks, null);
        }
        catch
        {
            // 将所有异常归一化为 Error，避免外部必须 try-catch
            return (LinkExtractionStatus.Error, null);
        }
    }

    /// <summary>
    /// 同步释放浏览器与页面资源。
    /// </summary>
    /// <remarks>
    /// 内部直接调用底层的同步 Dispose。若你希望优雅关闭浏览器，可改用 <see cref="DisposeAsync"/>。
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _page?.Dispose();
            _browser?.Dispose();
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 异步释放浏览器与页面资源。
    /// </summary>
    /// <returns>表示释放完成的任务。</returns>
    /// <remarks>
    /// - 关闭并释放 <see cref="IPage"/> 与 <see cref="IBrowser"/>；<br/>
    /// - 任何关闭过程中的异常均被吞掉以保证释放流程继续进行；<br/>
    /// - 释放完成后标记为已释放并调用 <see cref="GC.SuppressFinalize(object)"/>。
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_page is not null)
        {
            try { await _page.CloseAsync(); } catch { /* 忽略关闭异常 */ }
            try { await _page.DisposeAsync(); } catch { /* 忽略释放异常 */ }
            _page = null;
        }

        if (_browser is not null)
        {
            try { await _browser.CloseAsync(); } catch { /* 忽略关闭异常 */ }
            try { await _browser.DisposeAsync(); } catch { /* 忽略释放异常 */ }
            _browser = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}