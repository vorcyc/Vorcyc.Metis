namespace Vorcyc.Metis.Crawler.PageContentArchivers;

public sealed class ToutiaoPageContentArchiver : PageContentArchiver
{
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
