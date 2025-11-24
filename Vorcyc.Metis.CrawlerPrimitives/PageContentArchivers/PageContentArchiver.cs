using PuppeteerSharp;
using System.Security.Cryptography;
using System.Text;

namespace Vorcyc.Metis.Crawler.PageContentArchivers;

/// <summary>
/// 页面归档结果。
/// </summary>
/// <param name="Title">
/// 页面的人类可读标题（通常来源于锚点文本或页面标题）。非 null，可为空字符串。
/// </param>
/// <param name="Url">
/// 归档目标的绝对 URL；用于导航和生成唯一短哈希。非空非空白。
/// </param>
/// <param name="OutputFolder">
/// 成功保存到磁盘时的绝对目录；未保存或失败时为空字符串。
/// </param>
/// <param name="ImageCount">
/// 成功保存的图片数量（忽略失败项）。未落盘时为 0。
/// </param>
/// <param name="TextLength">
/// 主体纯文本的字符数（UTF-16 字符数，仅用于统计）。
/// </param>
/// <param name="Content">
/// 提取到的主体纯文本。无论是否落盘都返回；可为空字符串但不为 null。
/// </param>
/// <param name="Publisher">
/// 发布者/来源/作者。可为空。
/// </param>
/// <param name="PublishTime">
/// 发布时间。可为空。
/// </param>
/// <param name="Error">
/// 出错时的错误信息；成功为 null。
/// </param>
public sealed record ArchiveResult
(
    string Title,
    string Url,
    string OutputFolder,
    int ImageCount,
    int TextLength,
    string Content = "",
    string? Publisher = null,
    DateTimeOffset? PublishTime = null,
    string? Error = null
);

/// <summary>
/// 页面端提取结果模型（浏览器脚本返回的数据）。
/// </summary>
internal sealed class ExtractResult
{
    /// <summary>主体纯文本。</summary>
    public string? Text { get; set; }

    /// <summary>主体 HTML（如需保留结构或二次处理）。</summary>
    public string? Html { get; set; }

    /// <summary>主体内图片的 src 集合（可能为绝对、相对、协议相对或 data: URL）。</summary>
    public string[]? Images { get; set; }

    /// <summary>发布者（页面端提取的原始文本）。</summary>
    public string? Publisher { get; set; }

    /// <summary>发布时间（页面端提取的原始文本）。</summary>
    public string? PublishTime { get; set; }
}

/// <summary>
/// 基于 PuppeteerSharp 的通用页面内容归档器基类：
/// - 打开页面并等待加载稳定；
/// - 执行自定义 JS 提取器获取正文与图片；
/// - 可将正文保存为文本并按需下载图片；
/// - 支持对链接集合批量处理并返回结果。
/// </summary>
/// <remarks>
/// 子类必须提供 <see cref="ExtractContentSelector"/>，其值为在浏览器上下文执行的 JavaScript 函数字符串，
/// 且该函数应返回可序列化为 <see cref="ExtractResult"/> 的对象。
/// </remarks>
public abstract class PageContentArchiver : IDisposable
{
    /// <summary>
    /// 用于下载网络资源（如图片）的 <see cref="HttpClient"/>。在 <see cref="Dispose()"/> 时释放。
    /// </summary>
    protected HttpClient _httpClient;

    /// <summary>
    /// 初始化实例并创建默认的 <see cref="HttpClient"/>。
    /// </summary>
    public PageContentArchiver()
    {
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// 批量归档链接集合。
    /// </summary>
    /// <param name="links">要归档的链接集合。每项需包含非空的绝对 URL。</param>
    /// <param name="outputRoot">
    /// 归档输出根目录。为 null 时不落盘（仅提取并返回数据）；非 null 时每个链接会生成一个子目录。
    /// </param>
    /// <param name="navigationTimeoutMs">页面导航和加载等待的超时时间（毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。取消时会中断处理并抛出异常。</param>
    /// <returns>按输入顺序（URL 去重后）返回的每个链接的归档结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="links"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="outputRoot"/> 非 null 但为空或仅空白。</exception>
    /// <exception cref="OperationCanceledException">当 <paramref name="cancellationToken"/> 被取消时。</exception>
    /// <remarks>
    /// - 首次运行会通过 <see cref="BrowserFetcher"/> 下载 Chromium；<br/>
    /// - 单条链接的错误不会中断整体流程，错误写入对应项的 <see cref="ArchiveResult.Error"/>；<br/>
    /// - 当 <paramref name="outputRoot"/> 为 null 时，不进行任何磁盘写入。
    /// </remarks>
    public virtual async Task<IReadOnlyList<ArchiveResult>> ArchiveAsync(
                        IEnumerable<Link> links,
                        string? outputRoot = null,
                        int navigationTimeoutMs = 30000,
                        CancellationToken cancellationToken = default)
    {
        if (links is null) throw new ArgumentNullException(nameof(links));

        // 是否落盘：null 表示不保存；非 null 则要求为有效目录
        var saveToDisk = outputRoot is not null;
        if (saveToDisk && string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Output root cannot be empty or whitespace when provided.", nameof(outputRoot));

        // 如需保存则确保输出目录存在
        if (saveToDisk)
        {
            Directory.CreateDirectory(outputRoot!);
        }

        // 确保浏览器可用（首次会下载 Chromium）
        var fetcher = new BrowserFetcher();
        await fetcher.DownloadAsync();

        var results = new List<ArchiveResult>();

        // 启动无头浏览器；禁用 sandbox 适配容器/CI 环境；禁用 /dev/shm 限制以缓解共享内存不足
        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args = ["--no-sandbox", "--disable-dev-shm-usage"]
        });

        // 过滤无效 URL，并按 URL 去重
        var filteredLinks = links
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .DistinctBy(x => x!.Url);

        foreach (var item in filteredLinks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 为每个链接创建独立页面，避免串扰
            using var page = await browser.NewPageAsync();
            page.DefaultNavigationTimeout = navigationTimeoutMs;

            try
            {
                // 导航并等待网络空闲/页面加载，尽量保证内容稳定
                await page.GoToAsync(item!.Url!, new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.Networkidle0, WaitUntilNavigation.Load]
                });

                // 执行自定义提取脚本，获取正文数据与图片列表
                var extract = await ExtractMainContentAsync(page, cancellationToken);

                string folder = string.Empty;
                int imgCount = 0;

                // 统一计算文本与长度，未落盘场景也返回内容
                var text = extract.Text ?? string.Empty;
                var textLength = text.Length;

                if (saveToDisk)
                {
                    // 基于标题与 URL 短哈希构造输出目录
                    var safeTitle = PageContentArchiver.MakeFileNameSafe(string.IsNullOrWhiteSpace(item.Title) ? "untitled" : item.Title!);
                    if (safeTitle.Length > 60) safeTitle = safeTitle[..60];
                    var shortId = PageContentArchiver.ShortHash(item.Url!);
                    folder = Path.Combine(outputRoot!, $"{safeTitle}_{shortId}");
                    Directory.CreateDirectory(folder);

                    // 写入正文文本（UTF-8 无 BOM）
                    var textPath = Path.Combine(folder, "content.txt");
                    await System.IO.File.WriteAllTextAsync(
                        textPath,
                        text,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        cancellationToken);

                    // 下载图片（支持 http/https 与 data: URL）
                    imgCount = await PageContentArchiver.DownloadImagesAsync(
                        _httpClient,
                        page.Url,
                        extract.Images ?? Array.Empty<string>(),
                        folder,
                        cancellationToken);
                }

                // 尝试解析发布时间
                DateTimeOffset? published = null;
                if (!string.IsNullOrWhiteSpace(extract.PublishTime))
                {
                    if (DateTimeOffset.TryParse(extract.PublishTime, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), System.Globalization.DateTimeStyles.AssumeLocal, out var dto)
                        || DateTimeOffset.TryParse(extract.PublishTime, out dto))
                    {
                        published = dto;
                    }
                }

                // 汇总结果（未落盘时 OutputFolder 为空字符串，ImageCount 为 0）
                results.Add(new ArchiveResult(
                    Title: item.Title ?? string.Empty,
                    Url: item.Url!,
                    OutputFolder: folder,
                    ImageCount: imgCount,
                    TextLength: textLength,
                    Content: text,
                    Publisher: string.IsNullOrWhiteSpace(extract.Publisher) ? null : extract.Publisher,
                    PublishTime: published
                ));
            }
            catch (Exception ex)
            {
                // 单条失败不抛出：记录错误并继续
                results.Add(new ArchiveResult(
                    Title: item.Title ?? string.Empty,
                    Url: item.Url!,
                    OutputFolder: string.Empty,
                    ImageCount: 0,
                    TextLength: 0,
                    Content: string.Empty,
                    Publisher: null,
                    PublishTime: null,
                    Error: ex.Message
                ));
            }
        }

        return results;
    }

    /// <summary>
    /// 归档单个链接的便捷方法。
    /// </summary>
    /// <param name="link">要归档的链接。</param>
    /// <param name="outputRoot">输出根目录；为 null 时不落盘。</param>
    /// <param name="navigationTimeoutMs">页面导航与加载等待超时（毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该链接的归档结果。</returns>
    /// <exception cref="OperationCanceledException">当 <paramref name="cancellationToken"/> 被取消时。</exception>
    public virtual async Task<ArchiveResult> ArchiveAsync(
                        Link link,
                        string? outputRoot = null,
                        int navigationTimeoutMs = 30000,
                        CancellationToken cancellationToken = default)
    {
        var list = await ArchiveAsync([link], outputRoot, navigationTimeoutMs, cancellationToken);
        return list.FirstOrDefault() ?? new ArchiveResult(link.Title ?? string.Empty, link.Url ?? string.Empty, string.Empty, 0, 0, Content: string.Empty, Publisher: null, PublishTime: null, Error: "No result");
    }

    /// <summary>
    /// 浏览器端执行的 JavaScript 函数字符串，用于提取页面主体内容。
    /// </summary>
    /// <remarks>
    /// - 字符串应是可在浏览器上下文执行的函数（如 <c>function(){...}</c> 或箭头函数）；<br/>
    /// - 函数返回的对象应可序列化为 <see cref="ExtractResult"/>（包含 <c>Text</c>、<c>Html</c>、<c>Images</c> 等）。
    /// </remarks>
    protected abstract string ExtractContentSelector { get; }

    /// <summary>
    /// 在指定页面执行内容提取脚本并返回结果。
    /// </summary>
    /// <param name="page">当前页面。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析后的 <see cref="ExtractResult"/>；若为空，则返回空对象占位。</returns>
    private async Task<ExtractResult> ExtractMainContentAsync(IPage page, CancellationToken ct)
    {
        // 通过 EvaluateFunction 执行脚本，并将返回值反序列化为 ExtractResult
        var result = await page.EvaluateFunctionAsync<ExtractResult>(ExtractContentSelector);

        return result ?? new ExtractResult { Text = string.Empty, Html = string.Empty, Images = Array.Empty<string>() };
    }

    #region IDispose

    private bool _disposedValue;

    /// <summary>
    /// 释放当前实例所持有的资源。
    /// </summary>
    /// <param name="disposing">
    /// true 表示释放托管资源（如 <see cref="_httpClient"/>）；false 表示仅释放非托管资源。
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // 释放托管资源
                _httpClient?.Dispose();
            }

            // 释放非托管资源（若有）

            _disposedValue = true;
        }
    }

    // // 若引入非托管资源，可启用终结器，并在此调用 Dispose(false)
    // ~PageContentArchiver()
    // {
    //     Dispose(disposing: false);
    // }

    /// <summary>
    /// 释放资源并阻止终结器重复清理。
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region static helpers

    /// <summary>
    /// 将图片下载到指定目录，支持 http/https 与 data: URL。
    /// </summary>
    /// <param name="httpClient">用于下载图片的 <see cref="HttpClient"/>。</param>
    /// <param name="pageUrl">页面的绝对 URL（用于解析相对/协议相对地址）。</param>
    /// <param name="sources">图片地址集合，可能为绝对、相对、协议相对或 data: URL。</param>
    /// <param name="folder">目标保存目录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功保存的图片数量。单个失败会跳过并尝试保持序号连续。</returns>
    /// <remarks>
    /// - data: URL 会解析并按 MIME 推断扩展名；<br/>
    /// - http/https 会尝试保留路径扩展名，若缺失则使用 <c>.img</c>；<br/>
    /// - 单个下载失败不会中断整体流程。
    /// </remarks>
    internal static async Task<int> DownloadImagesAsync(HttpClient httpClient, string pageUrl, IEnumerable<string> sources, string folder, CancellationToken ct)
    {
        int count = 0;
        foreach (var src in sources.Distinct())
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // 处理 data: URL 内联图片
                if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var (ok, bytes, ext) = TryDecodeDataUrl(src);
                    if (!ok) continue;

                    var filePath = Path.Combine(folder, $"img_{++count:D3}{ext}");
                    await System.IO.File.WriteAllBytesAsync(filePath, bytes, ct);
                    continue;
                }

                // 绝对化 URL，并仅处理 http/https
                var abs = ToAbsoluteUrl(pageUrl, src);
                if (abs is null || (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps))
                    continue;

                var extName = Path.GetExtension(abs.AbsolutePath);
                if (string.IsNullOrEmpty(extName) || extName.Length > 6) extName = ".img";

                var path = Path.Combine(folder, $"img_{++count:D3}{extName}");
                var bytesHttp = await httpClient.GetByteArrayAsync(abs, ct);
                await System.IO.File.WriteAllBytesAsync(path, bytesHttp, ct);
            }
            catch
            {
                // 忽略单张失败，回退计数以保持序号连续（001、002、003…）
                count--;
            }
        }
        return Math.Max(0, count);
    }

    /// <summary>
    /// 将相对或协议相对地址转换为绝对 URL。
    /// </summary>
    /// <param name="baseUrl">页面的绝对基准 URL。</param>
    /// <param name="src">图片的原始 src 值。</param>
    /// <returns>解析后的绝对 <see cref="Uri"/>；无法解析时为 null。</returns>
    internal static Uri? ToAbsoluteUrl(string baseUrl, string src)
    {
        if (string.IsNullOrWhiteSpace(src)) return null;

        // 协议相对 //example.com/img.png
        if (src.StartsWith("//"))
        {
            var b = new Uri(baseUrl);
            return new Uri($"{b.Scheme}:{src}");
        }

        if (Uri.TryCreate(src, UriKind.Absolute, out var abs))
            return abs;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var bUri))
            return null;

        if (Uri.TryCreate(bUri, src, out var combined))
            return combined;

        return null;
    }

    /// <summary>
    /// 生成短且稳定的哈希字符串（基于输入，如 URL）。
    /// </summary>
    /// <param name="input">参与哈希的输入字符串。</param>
    /// <returns>12 个十六进制字符的短哈希（取 SHA-256 前 6 字节）。</returns>
    internal static string ShortHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes.AsSpan(0, 6)); // 12 hex chars
    }

    /// <summary>
    /// 将字符串转换为文件名安全形式。
    /// </summary>
    /// <param name="name">原始名称。</param>
    /// <returns>替换非法字符后的文件名；若结果为空返回 "untitled"。</returns>
    internal static string MakeFileNameSafe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        }
        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "untitled" : result;
    }

    /// <summary>
    /// 解析 data: URL，返回字节与建议扩展名。
    /// </summary>
    /// <param name="dataUrl">形如 data:[mime][;base64],payload 的 data: URL。</param>
    /// <returns>
    /// 三元组：
    /// <list type="bullet">
    ///   <item><description><c>ok</c>：是否解析成功。</description></item>
    ///   <item><description><c>bytes</c>：解析出的字节数据。</description></item>
    ///   <item><description><c>ext</c>：根据 MIME 推断的扩展名。</description></item>
    /// </list>
    /// </returns>
    internal static (bool ok, byte[] bytes, string ext) TryDecodeDataUrl(string dataUrl)
    {
        try
        {
            // 形式：data:image/png;base64,XXXX
            var comma = dataUrl.IndexOf(',');
            if (comma <= 0) return (false, Array.Empty<byte>(), string.Empty);

            var meta = dataUrl[..comma];
            var payload = dataUrl[(comma + 1)..];

            var isBase64 = meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
            var mime = meta.Split(':').ElementAtOrDefault(1)?.Split(';').FirstOrDefault() ?? "application/octet-stream";
            var ext = MimeToExtension(mime);

            var bytes = isBase64 ? Convert.FromBase64String(payload) : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return (true, bytes, ext);
        }
        catch
        {
            return (false, Array.Empty<byte>(), string.Empty);
        }
    }

    /// <summary>
    /// 简单的 MIME → 扩展名映射。
    /// </summary>
    /// <param name="mime">MIME 类型（不区分大小写）。</param>
    /// <returns>对应文件扩展名；未知类型返回 <c>.img</c>。</returns>
    internal static string MimeToExtension(string mime) => mime.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        _ => ".img"
    };

    #endregion
}