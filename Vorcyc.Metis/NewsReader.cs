using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Speech.Synthesis;
using System.Text;
using Vorcyc.Metis.Classifiers.Text;
using Vorcyc.Metis.Storage.SQLiteStorage;

namespace Vorcyc.Metis;

internal static class NewsReader
{



    private static int _index;


    private static SpeechSynthesizer _synth;

    private static List<ArchiveEntity> _history;


    public static async Task InitAsync()
    {
        await Task.Run(() =>
        {

            _synth = new SpeechSynthesizer();
            // Configure synthesizer defaults
            _synth.SetOutputToDefaultAudioDevice();
            _synth.Volume = 100; // 0-100
            _synth.Rate = 2;     // -10 to 10
                                 // Optionally select a specific voice if available:
            _synth.SelectVoice("Microsoft Huihui Desktop"); // Example for zh-CN

           

            _history = new();

            var last = DbHelper.GetLast(10);
            _history.AddRange(last);

            _index = 0;


        });
    }


    public static void Prevoius()
    {
        if (_index == 0)
        {

            var radnomArticle = DbHelper.GetRandomExcept(_history);
            if (radnomArticle is null)
            {
                _synth.SpeakAsync("没有更多的新闻了");
            }
            else
            {
                _index = 0;
                _history.Insert(0, radnomArticle);
                ReadArticle(radnomArticle);
            }
        }
        else
        {
            _index--;
            var article = _history[_index];
            ReadArticle(article);

        }

        Debug.WriteLine(_index);
    }



    public static void Next()
    {

        if (_index == _history.Count - 1)
        {

            var lastBatch = DbHelper.GetRandomBatchExcept(_history, lessThanDays: 3, count: 5);
            if (lastBatch is null || lastBatch.Count() == 0)
            {
                _synth.SpeakAsync("没有更多的新闻了");
                return;
            }
            else
            {
                _history.AddRange(lastBatch);
                _index++;
                var article = _history[_index];
                ReadArticle(article);
            }

        }
        else
        {
            _index++;
            var article = _history[_index];
            ReadArticle(article);
        }

        Debug.WriteLine(_index);
    }


    public static void ReadArticle(ArchiveEntity archive)
    {
        // Speak a sample text; replace with text from your UI if needed.
        // Example: if you have a TextBox named InputText, use: _synth.SpeakAsync(InputText.Text);
        _synth.SpeakAsyncCancelAll();

        var localFriendly = archive.PublishTime is DateTimeOffset dto
            ? ToFriendlyLocalString(dto)
            : "未知时间";

        var title = string.IsNullOrWhiteSpace(archive.Title) ? "无标题" : archive.Title.Trim();
        var author = string.IsNullOrWhiteSpace(archive.Publisher) ? "佚名" : archive.Publisher.Trim();
        var categoryText = PageCategoryBuilder.ToFriendlyChinese(archive.Category);
        var contentText = string.IsNullOrWhiteSpace(archive.Content) ? "内容为空。" : archive.Content.Trim();

        // 可选：控制正文长度，避免一次朗读过长
        const int maxBodyLength = 800;
        if (contentText.Length > maxBodyLength)
        {
            contentText = contentText[..maxBodyLength] + "……";
        }

        var content = $"{title}。作者：{author}。发布于{localFriendly}。分类：{categoryText}。正文：{contentText}";
        _synth.SpeakAsync(content);
    }

    // Convert DateTimeOffset to local time and format to a friendly string.
    private static string ToFriendlyLocalString(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime().DateTime;

        var now = DateTime.Now;
        var today = now.Date;
        var date = local.Date;

        // Today / Yesterday / The day before yesterday
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

        // Within the last 7 days: show weekday + time
        if ((today - date).TotalDays <= 7)
        {
            var weekDay = local.ToString("dddd"); // localized weekday
            return $"{weekDay} {local:HH:mm}";
        }

        // Same year: show month-day + time; otherwise show full date
        if (local.Year == now.Year)
        {
            return $"{local:MM-dd HH:mm}";
        }

        return $"{local:yyyy-MM-dd HH:mm}";
    }
}
