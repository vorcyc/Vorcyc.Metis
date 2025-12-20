using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vorcyc.Metis.Classifiers.Text;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis;

/// <summary>
/// 提供新闻朗读功能（上一条/下一条浏览、语音朗读、自动连播、配置持久化）。
/// </summary>
/// <remarks>
/// 使用 <see cref="Instance"/> 获取单例；调用 <see cref="InitAsync"/> 初始化；
/// 通过 <see cref="Previous"/> 与 <see cref="Next"/> 浏览；
/// 设置 <see cref="AutoPlay"/> 控制自动连播；
/// 通过 <see cref="SelectedCategory"/> 控制分类过滤；
/// 朗读完成时（未开启自动连播）通过 <see cref="PlaybackCompleted"/> 通知 UI 复位播放按钮；
/// 关闭时会在 <see cref="Dispose"/> 中自动保存配置（也可手动调用 <see cref="SaveConfigSafe"/>）。
/// </remarks>
internal sealed class NewsReader : IDisposable
{
    #region Fields

    private int _index;                            // 当前历史列表索引
    private SpeechSynthesizer? _synth;             // 语音合成器实例
    private readonly List<ArchiveEntity> _history; // 已加载/播放的文章历史（顺序列表）

 
    private static readonly string ConfigPath = "newsreader.config.json";

    // 自动连播开关（朗读完成后是否自动播放下一条）
    public bool AutoPlay { get; set; } = true;

    // 播放状态（可由外部设置：true开始播放当前文章，false停止播放）
    private bool _isPlaying;

    #endregion

    #region Properties

    /// <summary>
    /// 当前是否处于播放状态。设为 <c>true</c> 将开始播放当前文章（若无则提示并回退）；设为 <c>false</c> 将停止播放。
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;

            if (_isPlaying)
            {
                // 开始播放当前文章；若历史为空则提示并复位状态
                if (_history.Count == 0)
                {
                    Speak("没有更多的新闻了");
                    _isPlaying = false;
                    return;
                }

                // 修正索引至合法范围
                _index = Math.Clamp(_index, 0, Math.Max(0, _history.Count - 1));
                var current = _history[_index];
                ReadArticle(current);
            }
            else
            {
                // 外部要求停止播放
                CancelSpeaking();
            }
        }
    }

    /// <summary>
    /// 选中的内容分类（支持 Flags 组合）。用于随机获取文章时的分类过滤。
    /// </summary>
    public PageContentCategory SelectedCategory { get; set; } = PageContentCategory.Tech;

    #endregion

    #region Ctor & Singleton

    /// <summary>
    /// 私有构造，仅通过 <see cref="Instance"/> 获取。
    /// </summary>
    private NewsReader()
    {
        _history = new List<ArchiveEntity>();
        _index = 0;
    }

    private static NewsReader? s_instance = null;

    /// <summary>
    /// 单例实例。
    /// </summary>
    public static NewsReader Instance => s_instance ??= new NewsReader();

    #endregion

    #region Initialization & Disposal

    /// <summary>
    /// 异步初始化语音合成器并加载初始历史文章；在此之前会尝试加载配置（自动连播、分类）。
    /// </summary>
    public async Task InitAsync()
    {
        await Task.Run(() =>
        {
            // 加载配置（AutoPlay、SelectedCategory）
            LoadConfigSafe();

            // 构建语音合成器
            _synth = new SpeechSynthesizer();
            _synth.SetOutputToDefaultAudioDevice();
            _synth.Volume = 100; // 0-100
            _synth.Rate = 2;     // -10..10，语速
            _synth.SelectVoice("Microsoft Huihui Desktop");

            // 维护播放状态与自动连播
            _synth.SpeakStarted += Synth_SpeakStarted;
            _synth.SpeakCompleted += Synth_SpeakCompleted;

            // 初始化历史
            _history.Clear();
            var last = DbHelper.GetLast();
            if (last is not null && last.Length > 0)
            {
                _history.AddRange(last);
            }

            _index = 0;
        });
    }

    /// <summary>
    /// 释放语音合成器等资源，并安全保存配置。
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

        if (_synth is not null)
        {
            _synth.SpeakStarted -= Synth_SpeakStarted;
            _synth.SpeakCompleted -= Synth_SpeakCompleted;
        }

        _synth?.Dispose();
        _synth = null;

        // 退出时保存配置（忽略错误）
        SaveConfigSafe();
    }

    #endregion

    #region Navigation (Previous/Next)

    /// <summary>
    /// 浏览到上一条并朗读；在索引为 0 时，会随机补充上一条（按 <see cref="SelectedCategory"/> 过滤）。
    /// </summary>
    public void Previous()
    {
        if (_index == 0)
        {
            var randomArticle = DbHelper.GetRandomExcept(_history, SelectedCategory);
            if (randomArticle is null)
            {
                Speak("没有更多的新闻了");
                _isPlaying = false;
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
    /// 浏览到下一条并朗读；在到达末尾时，会随机批量补充（优先按 <see cref="SelectedCategory"/> 过滤，时间范围由近及远回退到 7/30/365 天，最后再尝试不按分类过滤）。
    /// </summary>
    public void Next()
    {
        if (_history.Count == 0)
        {
            Speak("没有更多的新闻了");
            _isPlaying = false;
            return;
        }

        if (_index >= _history.Count - 1)
        {
            // 末尾：尝试扩充随机批次（分类过滤→不过滤，近→远）
            var lastBatch = DbHelper.GetRandomBatchExcept(_history, SelectedCategory, lessThanDays: 7, maxCount: 5);
            if (lastBatch is null || !lastBatch.Any())
            {
                lastBatch = DbHelper.GetRandomBatchExcept(_history, SelectedCategory, lessThanDays: 30, maxCount: 5);
                if (lastBatch is null || !lastBatch.Any())
                {
                    lastBatch = DbHelper.GetRandomBatchExcept(_history, SelectedCategory, lessThanDays: 365, maxCount: 5);
                    if (lastBatch is null || !lastBatch.Any())
                    {
                        lastBatch = DbHelper.GetRandomBatchExcept(_history, lessThanDays: 7, maxCount: 5);
                        if (lastBatch is null || !lastBatch.Any())
                        {
                            lastBatch = DbHelper.GetRandomBatchExcept(_history, lessThanDays: 30, maxCount: 5);
                            if (lastBatch is null || !lastBatch.Any())
                            {
                                lastBatch = DbHelper.GetRandomBatchExcept(_history, lessThanDays: 365, maxCount: 5);
                                if (lastBatch is null || !lastBatch.Any())
                                {
                                    Speak("没有更多的新闻了");
                                    _isPlaying = false;
                                    return;
                                }
                            }
                        }
                    }
                }
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

    #endregion

    #region Reading

    /// <summary>
    /// 朗读指定文章的标题、作者、时间、分类与正文摘要。正文过长时会截断。
    /// </summary>
    /// <param name="archive">文章实体。</param>
    private void ReadArticle(ArchiveEntity? archive)
    {
        if (archive is null)
        {
            Speak("没有更多的新闻了");
            _isPlaying = false;
            return;
        }

        // 停止之前的朗读，避免重叠
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

        // 组合朗读文本
        var content = $"{title}。作者：{author}。时间：{localFriendly}。分类：{categoryText}。正文：{contentText}";
        Speak(content);

        // 通知 UI
        ArticleChanged?.Invoke(archive);
    }

    /// <summary>
    /// UI 可监听：播放完成通知（非取消）。当 <see cref="AutoPlay"/> 为 false 时用于让播放按钮复位。
    /// 参数 <paramref name="bool"/> 表示是否为取消导致的完成（取消时为 true）。
    /// </summary>
    public event Action<bool>? PlaybackCompleted;

    /// <summary>
    /// 朗读完成事件：更新播放状态，通知 UI，并在自动连播开启时继续播放下一条。
    /// </summary>
    private void Synth_SpeakCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        _isPlaying = false;

        // 通知 UI（用于未开启自动连播时复位播放按钮）
        PlaybackCompleted?.Invoke(e.Cancelled);

        // 自动连播：朗读正常完成时继续下一条
        if (AutoPlay && !e.Cancelled)
        {
            Next();
        }
    }

    /// <summary>
    /// 朗读开始事件：置播放状态为 true。
    /// </summary>
    private void Synth_SpeakStarted(object? sender, SpeakStartedEventArgs e)
    {
        _isPlaying = true;
    }

    /// <summary>
    /// 取消所有正在进行的异步朗读（安全，忽略异常）。
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
    /// 使用语音合成器异步朗读文本。
    /// </summary>
    /// <param name="text">要朗读的文本。</param>
    private void Speak(string text)
    {
        if (_synth is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // 先取消当前朗读，再开始新的异步朗读
        _synth.SpeakAsyncCancelAll();
        _synth.SpeakAsync(text);
    }

    /// <summary>
    /// 当前文章变更事件（UI 可订阅以更新界面）。
    /// </summary>
    public event Action<ArchiveEntity>? ArticleChanged;

    #endregion

    #region Utilities

    /// <summary>
    /// 将 <see cref="DateTimeOffset"/> 转换为本地时间的友好字符串（今天/昨天/前天/本周/同年/跨年）。
    /// </summary>
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

    #endregion

    #region Config (Load/Save)

    /// <summary>
    /// 配置数据模型（持久化 AutoPlay 与 SelectedCategory）。
    /// </summary>
    private sealed class NewsReaderConfig
    {
        public bool AutoPlay { get; init; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PageContentCategory SelectedCategory { get; init; }
    }

    /// <summary>
    /// 加载配置（安全，不抛异常）。不存在则创建目录并返回默认配置。
    /// </summary>
    private void LoadConfigSafe()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return;
            }

            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<NewsReaderConfig>(json);
            if (cfg is null)
            {
                return;
            }

            AutoPlay = cfg.AutoPlay;
            SelectedCategory = cfg.SelectedCategory;
        }
        catch
        {
            // 忽略配置读取异常
        }
    }

    /// <summary>
    /// 保存配置（安全，不抛异常）。建议在用户修改设置时调用，或在 <see cref="Dispose"/> 中统一保存。
    /// </summary>
    public void SaveConfigSafe()
    {
        try
        {

            var cfg = new NewsReaderConfig
            {
                AutoPlay = AutoPlay,
                SelectedCategory = SelectedCategory
            };

            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });

            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 忽略配置写入异常
        }
    }

    #endregion
}