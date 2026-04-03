using PuppeteerSharp;

namespace Vorcyc.Metis.CrawlerPrimitives.PageContentArchivers;

/// <summary>
/// 今日头条页面内容归档器：提取 div.article-content 内的正文、图片、发布者及发布时间。
/// </summary>
public sealed class ToutiaoPageContentArchiver : PageContentArchiver
{
    /// <summary>
    /// 初始化头条归档器，注入共享浏览器实例。
    /// </summary>
    /// <param name="browser">共享的无头浏览器实例。</param>
    public ToutiaoPageContentArchiver(IBrowser browser) : base(browser)
    {
    }

    /// <inheritdoc />
    protected override string ExtractContentSelector =>
        @"() => {
            const root = document.querySelector('div.article-content');
            if (!root) return { text: '', html: '', images: [], publishTime: '', publisher: '' };

            // 提取 meta 信息：第一个 span 为时间，第三个 span 为发布者
            const meta = root.querySelector('div.article-meta');
            const spans = meta ? Array.from(meta.querySelectorAll('span')) : [];
            const publishTime = (spans[0]?.innerText || '').trim();
            const publisher = (spans[2]?.innerText || '').trim();

            const uniq = (arr) => Array.from(new Set(arr));
            const imgs = uniq(
                Array.from(root.querySelectorAll('img'))
                    .map(img => img.getAttribute('src')
                                || img.getAttribute('data-src')
                                || img.getAttribute('data-original')
                                || img.getAttribute('data-actualsrc')
                                || '')
                    .filter(Boolean)
            );

            const text = (root.innerText || '').trim();
            const html = root.innerHTML || '';
            return { text, html, images: imgs, publishTime, publisher };
        }";
}
