using System.IO;
using System.Text.Json;

namespace Vorcyc.Metis;

/// <summary>
/// 应用程序全局设置（单例），支持异步加载/保存到 JSON 文件。
/// </summary>
public sealed class ApplicationSettings
{


    /// <summary>设置文件名。</summary>
    private const string file = "settings.json";

    /// <summary>
    /// 关闭主窗口时是否隐藏到系统托盘（而非退出应用）。
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// 从磁盘异步加载设置；若文件不存在则保留默认值。
    /// </summary>
    public async Task LoadAsync()
    {
        if (!File.Exists(GetSettingsPath()))
        {
            return;
        }

        await using var fs = File.Open(GetSettingsPath(), FileMode.Open, FileAccess.Read, FileShare.Read);
        var incoming = await JsonSerializer.DeserializeAsync<ApplicationSettings>(fs, SerializerOptions);
        if (incoming is null)
        {
            return;
        }

        CloseToTray = incoming.CloseToTray;
    }

    /// <summary>
    /// 将当前设置异步保存到磁盘。
    /// </summary>
    public async Task SaveAsync()
    {
        using var fs = File.Open(GetSettingsPath(), FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync<ApplicationSettings>(fs, this, SerializerOptions);
    }

    /// <summary>
    /// 获取设置文件的完整路径。
    /// </summary>
    private static string GetSettingsPath()
    {
        return file;
    }

    /// <summary>
    /// JSON 序列化/反序列化选项（格式化输出、允许尾逗号、忽略注释）。
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };






    private static ApplicationSettings? s_instance = null;

    /// <summary>
    /// 单例实例。
    /// </summary>
    public static ApplicationSettings Instance => s_instance ??= new();
}