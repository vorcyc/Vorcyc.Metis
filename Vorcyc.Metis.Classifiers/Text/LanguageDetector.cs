using System.Text.RegularExpressions;

namespace Vorcyc.Metis.Classifiers.Text;

/// <summary>
/// 语言检测器类
/// </summary>
public class LanguageDetector
{
    // 全局静态选项（对所有实例生效）
    private static DetectionOptions _options = new();
    public static DetectionOptions Options
    {
        get => _options;
        set
        {
            _options = value ?? new DetectionOptions();
            _options.Logger ??= (msg) => Console.WriteLine($"[LOG] {msg}");  // 默认控制台日志
        }
    }

    static LanguageDetector()
    {
        // 确保默认 Logger
        Options.Logger ??= (msg) => Console.WriteLine($"[LOG] {msg}");
    }

    // 修复：英文范围改为数组，支持多范围（A-Z 和 a-z）
    private static readonly Dictionary<LanguageType, string[]> _unicodeRanges = new()
    {
        { LanguageType.Chinese, new[] { "\u4e00-\u9fff" } },  // 汉字范围（简繁体）
        { LanguageType.English, new[] { "A-Z", "a-z" } }      // 英文范围：大写和小写分开
    };

    /// <summary>
    /// 构造函数，支持自定义选项
    /// </summary>
    /// <param name="options">检测选项（可选，若提供则设置为全局静态选项）</param>
    public LanguageDetector(DetectionOptions? options = null)
    {
        if (options is not null)
        {
            Options = options; // 设置为全局选项
        }
    }

    /// <summary>
    /// 检测文本的主要语言（实例方法）
    /// </summary>
    /// <param name="text">输入文本</param>
    /// <returns>语言类型枚举</returns>
    /// <exception cref="LanguageDetectionException">输入无效</exception>
    public LanguageType Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Options.Logger?.Invoke("输入文本为空");
            //throw new LanguageDetectionException("输入文本不能为空");
            return LanguageType.Unknown;
        }

        string preview = text.Length > 20 ? text.Substring(0, 20) + "..." : text;
        Options.Logger?.Invoke($"开始检测文本: '{preview}'");

        // 清理文本：移除非字母/汉字（如果配置）
        string cleanedText = Options.IgnoreNonLetters
            ? Regex.Replace(text, @"[^\w\u4e00-\u9fff]", string.Empty)
            : text;

        if (string.IsNullOrEmpty(cleanedText))
        {
            Options.Logger?.Invoke("清理后文本为空，无法判断");
            return LanguageType.Unknown;
        }

        // 计数
        int chineseCount = CountChars(cleanedText, _unicodeRanges[LanguageType.Chinese]);
        int englishCount = CountChars(cleanedText, _unicodeRanges[LanguageType.English]);

        int totalCount = chineseCount + englishCount;
        if (totalCount == 0)
        {
            Options.Logger?.Invoke("无有效字符，无法判断");
            return LanguageType.Unknown;
        }

        double chineseRatio = (double)chineseCount / totalCount;
        Options.Logger?.Invoke($"清理后总字符: {cleanedText.Length}，汉字计数: {chineseCount}, 英文计数: {englishCount}, 汉字占比: {chineseRatio:P2}");

        // 判断：如果只有汉字，直接 Chinese；否则用阈值
        LanguageType result;
        if (englishCount == 0 && chineseCount > 0)
        {
            result = LanguageType.Chinese;
        }
        else if (chineseCount == 0 && englishCount > 0)
        {
            result = LanguageType.English;
        }
        else
        {
            result = chineseRatio > Options.ChineseThreshold ? LanguageType.Chinese : LanguageType.English;
        }

        Options.Logger?.Invoke($"检测结果: {result}");
        return result;
    }

    /// <summary>
    /// 计数指定 Unicode 范围数组内的字符（私有辅助方法）
    /// </summary>
    private static int CountChars(string text, string[] ranges)
    {
        int count = 0;
        foreach (char c in text)
        {
            foreach (string range in ranges)
            {
                if (IsInRange(c, range))
                {
                    count++;
                    break;  // 匹配一个范围即可，避免重复
                }
            }
        }
        return count;
    }

    /// <summary>
    /// 检查字符是否在单个 Unicode 范围（格式如 "A-Z" 或 "\u4e00-\u9fff"）
    /// </summary>
    private static bool IsInRange(char c, string range)
    {
        if (!range.Contains('-'))
        {
            return c.ToString() == range;
        }

        var parts = range.Split('-');
        if (parts.Length != 2) return false;

        // 支持 Unicode char（C# char 可表示 \u4e00 等）
        if (!char.TryParse(parts[0], out char start) || !char.TryParse(parts[1], out char end))
            return false;

        return c >= start && c <= end;
    }

    /// <summary>
    /// 静态便捷方法（保持向后兼容）
    /// 注意：为尊重静态 Options，本方法会临时修改阈值并在完成后恢复。
    /// </summary>
    /// <param name="text"></param>
    /// <param name="threshold"></param>
    /// <returns></returns>
    public static LanguageType Detect(string? text, double threshold = 0.5)
    {
        double prevThreshold = Options.ChineseThreshold;
        try
        {
            Options.ChineseThreshold = threshold;
            var detector = new LanguageDetector();
            return detector.Detect(text);
        }
        finally
        {
            Options.ChineseThreshold = prevThreshold;
        }
    }

    /// <summary>
    /// 语言类型枚举
    /// </summary>
    public enum LanguageType
    {
        /// <summary>
        /// 中文
        /// </summary>
        Chinese,
        /// <summary>
        /// 英文
        /// </summary>
        English,
        /// <summary>
        /// 无法判断
        /// </summary>
        Unknown
    }

    /// <summary>
    /// 检测选项配置
    /// </summary>
    public class DetectionOptions
    {
        /// <summary>
        /// 汉字占比阈值（默认 0.5）
        /// </summary>
        public double ChineseThreshold { get; set; } = 0.5;

        /// <summary>
        /// 是否忽略数字和符号（默认 true，只统计字母/汉字）
        /// </summary>
        public bool IgnoreNonLetters { get; set; } = true;

        /// <summary>
        /// 日志输出动作（默认空）
        /// </summary>
        public Action<string>? Logger { get; set; } = null;
    }

    /// <summary>
    /// 语言检测异常
    /// </summary>
    public class LanguageDetectionException : Exception
    {
        public LanguageDetectionException(string message) : base(message) { }
    }
}