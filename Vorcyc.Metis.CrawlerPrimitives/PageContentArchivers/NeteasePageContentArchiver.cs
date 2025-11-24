namespace Vorcyc.Metis.CrawlerPrimitives.PageContentArchivers;

public sealed class NeteasePageContentArchiver : PageContentArchiver
{
    protected override string ExtractContentSelector =>
        @"() => {
            const root = document.querySelector('div.post_main');
            if (!root) return { text: '', html: '', images: [], publishTime: '', publisher: '' };

            // 从 post_info 中解析发布时间与发布者
            const info = root.querySelector('div.post_info');
            let publishTime = '';
            let publisher = '';

            if (info) {
                const infoText = (info.textContent || '').trim();

                // 优先用日期时间正则从开头截取：YYYY-MM-DD HH:mm[:ss]
                const m = infoText.match(/^\s*(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}(?::\d{2})?)/);
                if (m && m[1]) {
                    publishTime = m[1].trim();
                } else {
                    // 备选：按全文宽空格+“来源:”切分，取其前面的部分
                    const idx = infoText.indexOf('　来源:');
                    if (idx > 0) {
                        publishTime = infoText.substring(0, idx).trim();
                    }
                }

                // 第一个 <a> 的 innerText 作为发布者
                const a = info.querySelector('a');
                if (a) {
                    publisher = (a.innerText || '').trim();
                }
            }

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

            // 注意键名与 C# ExtractResult 属性对应（publisher/publishTime）
            return { text, html, images: imgs, publishTime, publisher };
        }";
}