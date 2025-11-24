using PuppeteerSharp;
using System.Text.RegularExpressions;

namespace Vorcyc.Metis.CrawlerPrimitives.LinkExtractors;

/// <summary>
/// 静态页面链接提取器（实例版）。
/// </summary>
/// <remarks>
/// 适用于不依赖无限滚动/动态分页加载的普通静态页面：
/// - 仅等待 DOMContentLoaded 即开始提取；
/// - 不进行下拉滚动，不触发二次加载；
/// - 对标题进行规范化（折叠所有空白并 Trim）；
/// - 使用与 <see cref="ToutiaoLinkExtractor"/> 一致的黑名单标题进行过滤；
/// - 不抛出异常，任何异常均转化为 <see cref="LinkExtractionStatus.Error"/>；
/// - 若导航成功但未提取到有效链接，返回 <see cref="LinkExtractionStatus.NoLinks"/>。
/// </remarks>
public sealed class StaticPageLinkExtractor : IDisposable, IAsyncDisposable
{
    private IBrowser? _browser;
    private IPage? _page;
    private bool _disposed;
    private string? _lastUrl;

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StaticPageLinkExtractor));
    }

    /// <summary>
    /// 标题规范化：将所有空白字符折叠为单个空格，并去除首尾空白。
    /// </summary>
    /// <param name="s">原始标题文本。</param>
    /// <returns>若输入为空或全空白，返回空字符串；否则返回折叠后的单行文本。</returns>
    /// <remarks>
    /// 与 <see cref="ToutiaoLinkExtractor"/> 保持一致，便于共享黑名单并统一过滤逻辑。
    /// </remarks>
    private static string NormalizeTitle(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        return Regex.Replace(s.ReplaceLineEndings(" "), @"\s+", " ").Trim();
    }

    /// <summary>
    /// 标题黑名单（已做规范化）。
    /// </summary>
    /// <remarks>
    /// - 主要用于剔除页脚、法律声明、栏目导航等非内容类链接；
    /// - 该集合与 <see cref="ToutiaoLinkExtractor"/> 中的 <c>BannedTitles</c> 对齐；
    /// - 入库前均会通过 <see cref="NormalizeTitle(string?)"/> 进行规范化。
    /// </remarks>
    private static readonly HashSet<string> BannedTitles = new(
        new[]
        {
            "注册",
            "登录",
            "发布器",
            "首页",
            "站长力推信誉网投【5717.COM】集团直营★AG女优发牌★万人棋牌★捕鱼爆大奖★注册瓜分百万彩金",
            "【威尼斯人集团◆上市公司】★★顶级信誉★■★每月亿元返利★■★大额无忧★■★返水3.0%无上限★",
            "清除 Cookies",
            "Archiver",
            "WAP",

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

        // 确保 Chromium 可用（本地存在则跳过下载）
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true // 无头模式，便于自动化执行
        });

        _page = await _browser.NewPageAsync();

        // 设置导航超时，避免长时间等待
        _page.DefaultNavigationTimeout = 30_000;
    }

    /// <summary>
    /// 刷新当前页面。
    /// - 若当前页已导航（非 about:blank），则执行 Reload；
    /// - 若尚未导航且存在上次访问的 URL（_lastUrl），则跳转到该地址；
    /// - 否则返回 false。
    /// </summary>
    /// <param name="timeoutMs">导航超时时间（毫秒）。默认 30 秒。</param>
    /// <param name="waitUntil">等待的导航完成条件，默认 DOMContentLoaded。</param>
    /// <returns>返回是否刷新成功。</returns>
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

            var isBlank = string.IsNullOrWhiteSpace(_page!.Url) ||
                          _page.Url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

            var response = isBlank
                ? (_lastUrl is not null ? await _page.GoToAsync(_lastUrl, navOptions) : null)
                : await _page.ReloadAsync(navOptions);

            return response is not null && response.Ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 访问指定静态页面，提取页面中的 <c>a[href]</c> 超链接及其可见文本作为标题。
    /// </summary>
    /// <param name="url">目标页面地址（应为绝对 URL 且以 http/https 开头）。</param>
    /// <param name="pages">接口保留参数；本实现针对静态页，忽略该参数。</param>
    /// <returns>
    /// 返回元组：
    /// - <see cref="LinkExtractionStatus"/> status：执行状态；
    /// - <see cref="Link"/>[] anchors：提取到的链接与标题（可能为 <c>null</c>）。
    /// </returns>
    /// <remarks>
    /// 提取与过滤流程：
    /// 1. 等待 DOMContentLoaded（不进行滚动，不触发懒加载）；<br/>
    /// 2. 在浏览器端收集所有 <c>a[href]</c>，解析为绝对 URL；<br/>
    /// 3. 过滤掉非 http/https、<c>javascript:</c>/<c>mailto:</c>/<c>tel:</c> 等协议；<br/>
    /// 4. 标题为空的锚点会被丢弃；<br/>
    /// 5. 回到本地后做标题规范化、按 URL 去重；<br/>
    /// 6. 应用黑名单标题过滤；若被黑名单清空则退回到未过滤集合。<br/>
    /// 异常处理：任何异常均被捕获并返回 <see cref="LinkExtractionStatus.Error"/>。
    /// </remarks>
    public async Task<(LinkExtractionStatus status, Link[]? anchors)> GetPageLinksAndTitlesAsync(string url, int pages = 0)
    {
        ThrowIfDisposed();

        try
        {
            await EnsurePageAsync();

            _lastUrl = url;

            // 静态页通常在 DOMContentLoaded 后即可提取
            var response = await _page!.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 30_000
            });

            var hadNavIssue = response is null || !response.Ok;

            // 在浏览器端收集锚点（尽可能将 URL 解析为绝对形式）
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

                        const lower = absolute.toLowerCase();
                        // 仅保留 http/https
                        if (!(lower.startsWith('http://') || lower.startsWith('https://'))) continue;
                        // 丢弃非内容类协议
                        if (lower.startsWith('javascript:') || lower.startsWith('mailto:') || lower.startsWith('tel:')) continue;

                        // 取可见文本作为标题
                        let title = (a.textContent || '').replace(/\s+/g, ' ').trim();
                        if (!title) continue;

                        // 粗略校验 URL 格式
                        try { new URL(absolute); } catch { continue; }

                        out.push({ title, url: absolute });
                    }
                    return out;
                }
            ");

            // 标题规范化 + 按 URL 去重
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

            // 黑名单标题过滤
            var filtered = baseList
                .Where(a => !BannedTitles.Contains(a.Title!))
                .ToArray();

            if (filtered.Length > 0)
            {
                return (LinkExtractionStatus.Success, filtered);
            }

            // 若黑名单过滤后为空，但原始集合非空，则回退为未过滤集合
            if (baseList.Length > 0)
            {
                return (LinkExtractionStatus.Success, baseList);
            }

            return (hadNavIssue ? LinkExtractionStatus.NavigationFailed : LinkExtractionStatus.NoLinks, null);
        }
        catch
        {
            // 统一异常出口，避免外部必须 try-catch
            return (LinkExtractionStatus.Error, null);
        }
    }

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