using System.Diagnostics;
using System.Speech.Synthesis;
using Vorcyc.Metis.Classifiers.Text;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis;

/// <summary>
/// 提供新闻朗读功能的实例类，支持前后浏览与内容朗读。
/// </summary>
/// <remarks>
/// 在 WPF 应用中使用 <see cref="InitAsync"/> 进行异步初始化，然后调用 <see cref="Next"/> 或 <see cref="Previous"/> 浏览并朗读内容。
/// 使用完毕后请调用 <see cref="Dispose"/> 释放资源。
/// </remarks>
internal sealed class NewsReader : IDisposable
{
    private int _index;
    private SpeechSynthesizer? _synth;
    private readonly List<ArchiveEntity> _history;

    /// <summary>
    /// 初始化 <see cref="NewsReader"/> 的新实例。
    /// </summary>
    private NewsReader()
    {
        _history = new List<ArchiveEntity>();
        _index = 0;
    }

    /// <summary>
    /// 异步初始化语音合成器并加载初始历史文章。
    /// </summary>
    /// <returns>表示异步操作的任务。</returns>
    public async Task InitAsync()
    {
        await Task.Run(() =>
        {
            _synth = new SpeechSynthesizer();
            _synth.SetOutputToDefaultAudioDevice();
            _synth.Volume = 100; // 0-100
            _synth.Rate = 2;     // -10 to 10
            _synth.SelectVoice("Microsoft Huihui Desktop");

            _history.Clear();
            var last = DbHelper.GetLast();
            if (last is not null && last.Length > 0)
            {
                _history.AddRange(last);
            }

            _index = 0;
        });
    }


    public PageContentCategory SelectedCategory { get; set; } = PageContentCategory.Tech;


    /// <summary>
    /// 浏览到上一条并朗读；在索引为 0 时，会随机补充上一条。
    /// </summary>
    public void Previous()
    {
        //if (_history.Count == 0)
        //{
        //    Speak("没有更多的新闻了");
        //    return;
        //}

        if (_index == 0)
        {
            var randomArticle = DbHelper.GetRandomExcept(_history, SelectedCategory);
            if (randomArticle is null)
            {
                Speak("没有更多的新闻了");
            }
            else
            {
                _index = 0;
                _history.Insert(0, randomArticle);
                ReadArticle(randomArticle);
            }
        }
        else
        {
            _index = Math.Max(0, _index - 1);
            var article = _history[_index];
            ReadArticle(article);
        }

        Debug.WriteLine(_index);
    }

    /// <summary>
    /// 浏览到下一条并朗读；在到达末尾时，会随机批量补充。
    /// </summary>
    public void Next()
    {
        if (_history.Count == 0)
        {
            Speak("没有更多的新闻了");
            return;
        }

        if (_index >= _history.Count - 1)
        {
            var lastBatch = DbHelper.GetRandomBatchExcept(_history, SelectedCategory, lessThanDays: 7, maxCount: 5);
            if (lastBatch is null || !lastBatch.Any())
            {
                Speak("没有更多的新闻了");
                return;
            }

            _history.AddRange(lastBatch);
            _index = Math.Min(_history.Count - 1, _index + 1);
            var article = _history[_index];
            ReadArticle(article);
        }
        else
        {
            _index = Math.Min(_history.Count - 1, _index + 1);
            var article = _history[_index];
            ReadArticle(article);
        }

        Debug.WriteLine(_index);
    }

    /// <summary>
    /// 朗读指定文章内容。
    /// </summary>
    /// <param name="archive">文章实体。</param>
    private void ReadArticle(ArchiveEntity? archive)
    {
        if (archive is null)
        {
            Speak("没有更多的新闻了");
            return;
        }

        CancelSpeaking();

        var localFriendly = archive.PublishTime is DateTimeOffset dto
            ? ToFriendlyLocalString(dto)
            : "未知时间";

        var title = string.IsNullOrWhiteSpace(archive.Title) ? "无标题" : archive.Title.Trim();
        var author = string.IsNullOrWhiteSpace(archive.Publisher) ? "佚名" : archive.Publisher.Trim();
        var categoryText = PageCategoryBuilder.ToFriendlyChinese(archive.CategoryValue);
        var contentText = string.IsNullOrWhiteSpace(archive.Content) ? "内容为空。" : archive.Content.Trim();

        // 控制正文长度，避免一次朗读过长
        const int maxBodyLength = 800;
        if (contentText.Length > maxBodyLength)
        {
            contentText = contentText[..maxBodyLength] + "……";
        }

        // 更顺畅的语序与措辞
        var content = $"{title}。作者：{author}。时间：{localFriendly}。分类：{categoryText}。正文：{contentText}";
        Speak(content);

        ArticleChanged?.Invoke(archive);
    }


    public event Action<ArchiveEntity>? ArticleChanged;



    /// <summary>
    /// 将 UTC 或本地的 <see cref="DateTimeOffset"/> 转换为本地时间的友好字符串。
    /// </summary>
    /// <param name="utc">UTC 或本地时间。</param>
    /// <returns>友好本地时间文本，如“今天 14:35”、“昨天 09:12”、“MM月dd日 HH:mm”或“yyyy年MM月dd日 HH:mm”。</returns>
    internal static string ToFriendlyLocalString(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime().DateTime;

        var now = DateTime.Now;
        var today = now.Date;
        var date = local.Date;

        if (date == today)
        {
            return $"今天 {local:HH:mm}";
        }

        if (date == today.AddDays(-1))
        {
            return $"昨天 {local:HH:mm}";
        }

        if (date == today.AddDays(-2))
        {
            return $"前天 {local:HH:mm}";
        }

        // 使用本地文化的周起始日来判断“本周”
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
        var delta = (7 + (today.DayOfWeek - firstDayOfWeek)) % 7;
        var startOfWeek = today.AddDays(-delta);

        if (date >= startOfWeek && date < startOfWeek.AddDays(7))
        {
            var weekDay = local.ToString("dddd"); // 本地化的星期
            return $"{weekDay} {local:HH:mm}";
        }

        // 上周及更早：读出中文日期词语
        if (local.Year == now.Year)
        {
            return $"{local:MM}月{local:dd}日 {local:HH:mm}";
        }

        return $"{local:yyyy}年{local:MM}月{local:dd}日 {local:HH:mm}";
    }

    /// <summary>
    /// 取消所有正在进行的异步朗读。
    /// </summary>
    private void CancelSpeaking()
    {
        if (_synth is null)
        {
            return;
        }

        try
        {
            _synth.SpeakAsyncCancelAll();
        }
        catch
        {
            // 忽略取消异常
        }
    }

    /// <summary>
    /// 使用语音合成器朗读文本。
    /// </summary>
    /// <param name="text">要朗读的文本。</param>
    private void Speak(string text)
    {
        if (_synth is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _synth.SpeakAsyncCancelAll();
        _synth.SpeakAsync(text);
    }

    /// <summary>
    /// 释放语音合成器等非托管资源。
    /// </summary>
    public void Dispose()
    {
        try
        {
            _synth?.SpeakAsyncCancelAll();
        }
        catch
        {
            // 忽略取消异常
        }

        _synth?.Dispose();
        _synth = null;
    }


    private static NewsReader? s_instance = null;

    public static NewsReader Instance => s_instance ??= new NewsReader();
}