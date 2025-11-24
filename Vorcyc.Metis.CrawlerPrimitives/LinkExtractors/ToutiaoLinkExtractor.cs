using PuppeteerSharp;
using System.Text.RegularExpressions;

namespace Vorcyc.Metis.CrawlerPrimitives.LinkExtractors;

/// <summary>
/// 使用 PuppeteerSharp 抓取页面中的超链接并提取标题/URL，
/// 同时做面向今日头条（Toutiao）流式页面的基础过滤与清洗。
/// </summary>
/// <remarks>
/// 站点规则（仅用于理解过滤意图，不做强依赖）：
/// <list type="bullet">
/// <item>- 仅保留以 https://www.toutiao.com/article/ 开头的文章页链接；</item>
/// <item>- 其它类型链接（如 /w/、/w/video/）不返回。</item>
/// </list>
/// </remarks>
public sealed class ToutiaoLinkExtractor : IDisposable, IAsyncDisposable
{
    private readonly string _baseUrl;
    private IBrowser? _browser;
    private IPage? _page;
    private bool _disposed;

    public ToutiaoLinkExtractor(string baseUrl = "https://www.toutiao.com")
    {
        _baseUrl = baseUrl;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ToutiaoLinkExtractor));
    }

    private static string NormalizeTitle(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var collapsed = Regex.Replace(s.ReplaceLineEndings(" "), @"\s+", " ").Trim();
        return collapsed;
    }

    /// <summary>
    /// 需要过滤掉的（已标准化）标题集合。
    /// </summary>
    /// <remarks>
    /// 多为页脚、法律声明、栏目导航等非内容类锚点文本，
    /// 通过归一化后进行精确匹配，避免被误当作有效内容标题。
    /// </remarks>
    private static readonly HashSet<string> BannedTitles = new(
        new[]
        {
            "直播",
            "懂车帝",
            "下载头条APP",
            "关于头条",
            "侵权投诉",
            "凤凰卫视",
            "懂车时间",
            "驾享来电",
            "Yo哥真帅！（Yoko视频工作室）",
            "君子游网上围棋教室",
            "扫黄打非网上举报",
            "网络谣言曝光台",
            "网上有害信息举报",
            "侵权举报受理公示",
            "京ICP证140141号",
            "京ICP备12025439号-3",
            "网络文化经营许可证 京网文〔2023〕3628-111号",
            "营业执照",
            "广播电视节目制作经营许可证",
            "出版物经营许可证",
            "营业性演出许可证",
            "药品医疗器械网络信息服务备案编号：（京）网药械信息备字（2023）第00006号",
            "跟帖评论自律管理承诺书",
            "京公网安备 11000002002023号",
            "网信算备110108823483902220017号",
            "网信算备110108823483904220019号",
            "网信算备110108823483903230017号",
            "互联网宗教信息服务许可证：京（2025）0000021",
            "加入头条",
            "用户协议",
            "隐私政策",
            "媒体合作",
            "广告合作",
            "友情链接",
            "媒体报道",
            "产品合作",
            "头条MCN",
            "联系我们",
            "廉洁举报",
            "企业认证",
            "免责声明",
            "下载今日头条APP",
        }.Select(NormalizeTitle),
        StringComparer.Ordinal
    );

    private async Task EnsurePageAsync()
    {
        if (_page is not null && _browser is not null) return;

        // 确保 Chromium 可用（若本地已存在则跳过下载）。
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true
        });

        _page = await _browser.NewPageAsync();
        _page.DefaultNavigationTimeout = 30_000;
    }

    /// <summary>
    /// 刷新当前页面。
    /// - 若当前页已导航（非 about:blank），则执行 Reload；
    /// - 若尚未导航，则跳转到基础地址（_baseUrl）。
    /// </summary>
    /// <param name="timeoutMs">导航超时时间（毫秒）。默认 30 秒。</param>
    /// <param name="waitUntil">等待的导航完成条件，默认 DOMContentLoaded。</param>
    /// <returns>返回是否刷新成功。</returns>
    public async Task<bool> RefreshAsync(int timeoutMs = 30_000, WaitUntilNavigation waitUntil = WaitUntilNavigation.DOMContentLoaded)
    {
        ThrowIfDisposed();
        await EnsurePageAsync();

        //try
        //{
            var navOptions = new NavigationOptions
            {
                WaitUntil = [waitUntil],
                Timeout = timeoutMs
            };

            // 如果还在 about:blank，使用基础地址进行首次导航；否则执行页面重载
            var isBlank = string.IsNullOrWhiteSpace(_page!.Url) ||
                          _page.Url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

            var response = isBlank
                ? await _page.GoToAsync(_baseUrl, navOptions)
                : await _page.ReloadAsync(navOptions);

            return response is not null && response.Ok;
        //}
        //catch
        //{
        //    return false;
        //}
    }

    /// <summary>
    /// 访问实例配置的基础 URL，滚动加载若干页后，提取页面中的 <c>&lt;a href&gt;</c> 超链接及其文本标题。
    /// 仅返回以 https://www.toutiao.com/article/ 开头的文章链接。
    /// </summary>
    /// <param name="pages">下拉滚动加载的步数（越大理论上加载越多内容，默认 5）。</param>
    /// <returns>
    /// 返回元组：
    /// <list type="bullet">
    /// <item>
    /// <description><see cref="LinkExtractionStatus"/> status：执行状态；</description>
    /// </item>
    /// <item>
    /// <description><see cref="Link"/>[] anchors：提取到的链接与标题（可能为 <c>null</c>）。</description>
    /// </item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// 提取逻辑（浏览器端执行的 JS）已包含严格前缀过滤，仅保留以 https://www.toutiao.com/article/ 开头的链接。
    /// 本地侧亦做一次前缀校验作为防御性过滤。
    /// </remarks>
    public async Task<(LinkExtractionStatus status, Link[]? anchors)> GetPageLinksAndTitlesAsync(int pages = 5)
    {
        ThrowIfDisposed();

        try
        {
            await EnsurePageAsync();

            var response = await _page!.GoToAsync(_baseUrl, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 30_000
            });

            var hadNavIssue = response is null || !response.Ok;

            // 等待页面出现任意 a[href]（超时不抛，继续尝试）
            try
            {
                await _page.WaitForSelectorAsync("a[href]", new WaitForSelectorOptions { Timeout = 5_000 });
            }
            catch (WaitTaskTimeoutException)
            {
                // 未检测到链接也继续执行，最终会按空结果处理
            }

            // 向下滚动若干次以触发懒加载/无限流加载
            await LoadMoreByScrollingAsync(_page, pages: pages, delayPerStepMs: 500);

            // 在浏览器端执行 JS，收集页面中的链接及可见文本作为标题
            var items = await _page.EvaluateFunctionAsync<Link[]>(@"
                () => {
                    const out = [];
                    const anchors = document.querySelectorAll('a[href]');
                    for (const a of anchors) {
                        const hrefRaw = a.href?.trim();
                        if (!hrefRaw) continue;

                        // 仅保留以指定前缀开头的文章链接
                        if (!hrefRaw.startsWith('https://www.toutiao.com/article/')) continue;

                        const lower = hrefRaw.toLowerCase();
                        if (!(lower.startsWith('http://') || lower.startsWith('https://'))) continue;

                        let parsed;
                        try { parsed = new URL(hrefRaw); } catch { continue; }

                        // 过滤 '#comment'
                        if ((parsed.hash || '').toLowerCase() === '#comment') continue;

                        // 过滤 URL（不含 hash）以 '/?source=feed' 结尾
                        const hrefNoHash = `${parsed.origin}${parsed.pathname}${parsed.search}`;
                        if (hrefNoHash.endsWith('/?source=feed')) continue;

                        // 过滤包含 '/video' 的路径（如 /video/...）
                        const pathLower = parsed.pathname.toLowerCase();
                        if (pathLower.includes('/video')) continue;

                        let title = (a.textContent || '').replace(/\s+/g, ' ').trim();
                        if (!title) continue; // 跳过空标题

                        // 跳过“数字+评论”标题（如：20评论、53评论）
                        if (/^\d+\s*评论$/.test(title)) continue;

                        out.push({ title, url: parsed.href });
                    }
                    return out;
                }
            ");

            // 本地二次过滤：剔除黑名单标题 + 再次确认链接前缀
            if (items is not null && items.Length > 0)
            {
                var filtered = items
                    .Where(a => !string.IsNullOrWhiteSpace(a.Title))
                    .Where(a => a.Url?.StartsWith("https://www.toutiao.com/article/", StringComparison.Ordinal) == true)
                    .Where(a => !BannedTitles.Contains(NormalizeTitle(a.Title)))
                    .ToArray();

                if (filtered.Length > 0)
                {
                    return (LinkExtractionStatus.Success, filtered);
                }

                return (LinkExtractionStatus.Success, items);
            }

            return (hadNavIssue ? LinkExtractionStatus.NavigationFailed : LinkExtractionStatus.NoLinks, null);
        }
        catch (Exception)
        {
            // 统一捕获异常并返回 Error，避免调用侧必须 try-catch。
            return (LinkExtractionStatus.Error, null);
        }
    }

    /// <summary>
    /// 执行页面滚动以触发延迟加载/懒加载，尽可能加载更多内容。
    /// </summary>
    /// <param name="page">Puppeteer 页面实例。</param>
    /// <param name="pages">滚动次数（步数）。</param>
    /// <param name="delayPerStepMs">每次滚动后的等待时长（毫秒）。</param>
    /// <remarks>
    /// 实现策略：
    /// <para>- 每步向下滚动一个视口高度，并等待指定毫秒；</para>
    /// <para>- 若滚动后页面总高度未增长，则尝试直接跳至底部；若仍无变化则提前结束。</para>
    /// </remarks>
    private static async Task LoadMoreByScrollingAsync(IPage page, int pages = 5, int delayPerStepMs = 500)
    {
        double lastHeight = await page.EvaluateFunctionAsync<double>("() => document.scrollingElement?.scrollHeight || document.body.scrollHeight || 0");
        for (int i = 0; i < pages; i++)
        {
            await page.EvaluateFunctionAsync(@"() => window.scrollBy(0, window.innerHeight)");
            await Task.Delay(delayPerStepMs);

            double newHeight = await page.EvaluateFunctionAsync<double>("() => document.scrollingElement?.scrollHeight || document.body.scrollHeight || 0");

            // 若高度无增长，尝试跳到底部一次；仍无变化则提前结束
            if (newHeight <= lastHeight)
            {
                await page.EvaluateFunctionAsync(@"() => window.scrollTo(0, document.scrollingElement?.scrollHeight || document.body.scrollHeight || 0)");
                await Task.Delay(delayPerStepMs);
                newHeight = await page.EvaluateFunctionAsync<double>("() => document.scrollingElement?.scrollHeight || document.body.scrollHeight || 0");
                if (newHeight <= lastHeight) break;
            }

            lastHeight = newHeight;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 同步释放包装异步释放
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_page is not null)
        {
            try { await _page.CloseAsync(); } catch { /* ignore */ }
            try { await _page.DisposeAsync(); } catch { /* ignore */ }
            _page = null;
        }

        if (_browser is not null)
        {
            try { await _browser.CloseAsync(); } catch { /* ignore */ }
            try { await _browser.DisposeAsync(); } catch { /* ignore */ }
            _browser = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}