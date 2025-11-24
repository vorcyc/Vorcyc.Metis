namespace Vorcyc.Metis.Crawler.LinkExtractors;

/// <summary>
/// 链接提取过程的状态码。
/// </summary>
public enum LinkExtractionStatus
{
    /// <summary>
    /// 成功提取到链接（并返回集合）。
    /// </summary>
    Success = 0,
    /// <summary>
    /// 页面导航失败（超时或返回状态码非 2xx）。
    /// </summary>
    NavigationFailed = 1,
    /// <summary>
    /// 页面无可用链接（或未能找到符合条件的链接）。
    /// </summary>
    NoLinks = 2,
    /// <summary>
    /// 发生运行时异常（已捕获）。
    /// </summary>
    Error = 3
}
