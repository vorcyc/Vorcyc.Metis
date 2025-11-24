namespace Vorcyc.Metis.Crawler;

/// <summary>
/// 表示从页面提取到的锚点对象（Link），包含锚点文本、绝对链接以及可选的内容类别。
/// </summary>
/// <remarks>
/// - 本类型通常由链接提取器生成，例如：<see cref="LinkExtractors.ToutiaoLinkExtractor"/>、<see cref="LinkExtractors.NeteaseLinkExtractor"/> 等。<para/>
/// - 可通过调用 <see cref="BuildCategoryAsync"/> 对链接内容进行分类，结果写入 <see cref="Category"/>。在分类前该属性为 <see langword="null"/>。<para/>
/// - 本类型为可变对象（包含可设置的属性），在多线程环境下请进行适当的同步控制。
/// </remarks>
/// <example>
/// 以下示例展示了如何创建 <see cref="Link"/> 并构建其分类：
/// <code language="csharp">
/// var link = new Vorcyc.Metis.Crawler.Link
/// {
///     Title = "示例新闻：科技新品发布",
///     Url = "https://example.com/news/tech/123"
/// };
/// await link.BuildCategoryAsync();
/// // 现在 link.Category 可能为 PageContentCategory.Tech
/// </code>
/// </example>
/// <seealso cref="PageContentCategory"/>
/// <seealso cref="PageCategoryBuilder"/>
public class Link
{
    /// <summary>
    /// 锚点文本（作为标题使用，已保留原始页面文本）。
    /// </summary>
    /// <remarks>
    /// 可能包含来源页面中的原样空白/标点；若用于展示或存储，可根据需要自行清洗或截断。
    /// </remarks>
    public string? Title { get; set; }

    /// <summary>
    /// 锚点链接的绝对 URL。
    /// </summary>
    /// <remarks>
    /// 期望为标准化后的绝对地址（如已解析相对路径、移除多余片段等）。在使用前建议进行基本校验。
    /// </remarks>
    public string? Url { get; set; }



    /// <summary>
    /// 返回便于调试的字符串表示，格式为：<c>{Title} -&gt; {Url}</c>。
    /// </summary>
    /// <returns>当前链接的字符串表示。</returns>
    public override string ToString()
    {
        return $"{Title} -> {Url}";
    }
}